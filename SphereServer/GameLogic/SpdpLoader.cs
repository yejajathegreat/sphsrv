using SphereServer.Helpers;
using SphereServer.Network;
using SphereServer.Protocol;

namespace SphereServer.GameLogic;

/// <summary>
/// Represents a single field in an .spdp packet definition.
/// </summary>
public class PacketPart
{
    public string Name;
    public string Type; // UINT64, BITS, STRING, COORDS_CLIENT, BYTES
    public int BitLength;
    public List<int> Value; // bits in LSB-first order (reversed from .spdp file)

    public PacketPart(string name, string type, int bitLength, List<int> value)
    {
        Name = name;
        Type = type;
        BitLength = bitLength;
        Value = value;
    }
}

/// <summary>
/// Loads .spdp packet definition files and builds NPC spawn packets.
/// Ported from knelse PacketPart.cs (Shared/Networking/Packets/PacketPart.cs).
///
/// The .spdp format is TSV with columns:
///   0: field name
///   1: type (UINT64, BITS, STRING, COORDS_CLIENT, BYTES)
///   2: bit offset (reference only)
///   3: bit length (or "__fromPrevious")
///   4: enum name
///   5-8: RGBA colors (ignored)
///   9: binary value as '0'/'1' string (MSB-first in file, reversed to LSB-first on load)
/// </summary>
public static class SpdpLoader
{
    private const string LengthFromPrevious = "__fromPrevious";

    /// <summary>
    /// Load an .spdp file and return a list of PacketParts with values in LSB-first bit order.
    /// </summary>
    public static List<PacketPart> LoadFromFile(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        var parts = new List<PacketPart>();

        foreach (var line in lines)
        {
            var fields = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 10)
            {
                Console.WriteLine($"SpdpLoader: skipping line with {fields.Length} fields in {filePath}");
                continue;
            }

            var name = fields[0];
            var type = fields[1];

            // fields[2] = bit offset (reference only, not used)

            int bitLength;
            if (fields[3] == LengthFromPrevious)
            {
                // Length = integer value of previous part's bits (LSB-first)
                var prev = parts[^1];
                bitLength = BitsToInt(prev.Value);
                // Multiply by 8 since the previous field holds a byte count (string length)
                bitLength *= 8;
            }
            else
            {
                bitLength = int.Parse(fields[3]);
            }

            // fields[4] = enum name (ignored here)
            // fields[5-8] = RGBA colors (ignored)

            // fields[9] = binary value string, MSB-first in file
            // Reverse to get LSB-first order
            var binaryStr = fields[9];
            var value = new List<int>(bitLength);
            for (var i = binaryStr.Length - 1; i >= 0; i--)
            {
                value.Add(binaryStr[i] - '0');
            }

            parts.Add(new PacketPart(name, type, bitLength, value));
        }

        return parts;
    }

    /// <summary>
    /// Update the entity_id field with a 16-bit value.
    /// </summary>
    public static void UpdateEntityId(List<PacketPart> parts, ushort id)
    {
        var part = parts.FirstOrDefault(p => p.Name == "entity_id");
        if (part != null)
        {
            part.Value = IntToBits(id, 16);
        }
    }

    /// <summary>
    /// Update x, y, z coordinate fields and angle.
    /// Coordinates are encoded via CoordsHelper.EncodeServerCoordinate, then each byte's
    /// bits are stored LSB-first.
    /// </summary>
    public static void UpdateCoordinates(List<PacketPart> parts, double x, double y, double z, int angle)
    {
        var xBits = CoordToBits(x);
        var yBits = CoordToBits(y);
        var zBits = CoordToBits(z);
        var angleBits = IntToBits(angle, 8);

        foreach (var part in parts)
        {
            switch (part.Name)
            {
                case "x": part.Value = xBits; break;
                case "y": part.Value = yBits; break;
                case "z": part.Value = zBits; break;
                case "angle": part.Value = angleBits; break;
            }
        }
    }

    /// <summary>
    /// Update a named integer field.
    /// </summary>
    public static void UpdateIntValue(List<PacketPart> parts, string name, int value, int bitLength)
    {
        var part = parts.FirstOrDefault(p => p.Name == name);
        if (part != null)
        {
            part.Value = IntToBits(value, bitLength);
            part.BitLength = bitLength;
        }
    }

    /// <summary>
    /// Update a named string field. Encodes as Windows-1251 bytes, each byte's bits LSB-first.
    /// Also updates the corresponding length field if present.
    /// </summary>
    public static void UpdateStringValue(List<PacketPart> parts, string name, string value,
        bool updateLength = false, int lengthBitLength = 8)
    {
        var part = parts.FirstOrDefault(p => p.Name == name);
        if (part != null)
        {
            var bytes = SphereGameServer.Win1251.GetBytes(value);
            var bits = new List<int>();
            foreach (var b in bytes)
            {
                for (var i = 0; i < 8; i++)
                {
                    bits.Add((b >> i) & 1);
                }
            }
            part.Value = bits;
            part.BitLength = bits.Count;
        }

        if (updateLength)
        {
            UpdateIntValue(parts, name + "_length", value.Length, lengthBitLength);
        }
    }

    /// <summary>
    /// Build the final packet bytes from all parts.
    /// Writes all part values sequentially using BitWriter, wraps with packet header
    /// (padZeros=3), and post-processes (last byte = 0x00).
    /// </summary>
    public static byte[] GetBytesToWrite(List<PacketPart> parts)
    {
        var writer = new BitWriter();

        foreach (var part in parts)
        {
            writer.WriteBits(part.Value);
        }

        var content = writer.ToArray();
        var packet = PacketBuilder.Build(content, 3);

        // Post-process: set last byte to 0x00 (from NpcInteractable.PostprocessPacketBytes)
        packet[^1] = 0x00;

        return packet;
    }

    /// <summary>
    /// Convert an integer to a list of bits in LSB-first order.
    /// Matches knelse BitStreamExtensions.IntToBits.
    /// </summary>
    public static List<int> IntToBits(int val, int length)
    {
        var result = new List<int>(length);
        var v = val;
        while (v > 0)
        {
            result.Add(v & 1);
            v >>= 1;
        }
        while (result.Count < length)
        {
            result.Add(0);
        }
        return result;
    }

    /// <summary>
    /// Convert a list of bits (LSB-first) to an integer.
    /// Matches knelse BitStreamExtensions.BitsToInt.
    /// </summary>
    public static int BitsToInt(List<int> bits)
    {
        var result = 0;
        for (var i = bits.Count - 1; i >= 0; i--)
        {
            result <<= 1;
            result += bits[i];
        }
        return result;
    }

    /// <summary>
    /// Encode a coordinate value to 32 bits (4 bytes) in LSB-first order.
    /// Each byte is written LSB-first, matching BitStream.ReadBits behavior.
    /// </summary>
    private static List<int> CoordToBits(double value)
    {
        var bytes = CoordsHelper.EncodeServerCoordinate(value);
        var bits = new List<int>(32);
        foreach (var b in bytes)
        {
            for (var i = 0; i < 8; i++)
            {
                bits.Add((b >> i) & 1);
            }
        }
        return bits;
    }
}
