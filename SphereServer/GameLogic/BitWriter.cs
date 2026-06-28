using System.Text;
using SphereServer.Helpers;
using SphereServer.Network;

namespace SphereServer.GameLogic;

/// <summary>
/// Bit-level writer that works LSB-first, matching knelse's BitStream behavior.
/// Bits are packed into bytes starting from bit 0 (LSB) of each byte.
/// </summary>
public class BitWriter
{
    private readonly List<int> _bits = new();

    /// <summary>
    /// Write a single bit (0 or 1).
    /// </summary>
    public void WriteBit(int bit)
    {
        _bits.Add(bit & 1);
    }

    /// <summary>
    /// Write an integer as bits, LSB first.
    /// </summary>
    public void WriteBits(int value, int count)
    {
        for (var i = 0; i < count; i++)
        {
            _bits.Add((value >> i) & 1);
        }
    }

    /// <summary>
    /// Write from a list of bit values (already in LSB-first order internally).
    /// </summary>
    public void WriteBits(List<int> bits)
    {
        _bits.AddRange(bits);
    }

    /// <summary>
    /// Write bits from a reversed string representation.
    /// The .spdp files store bits MSB-first as text, but on load the string is reversed
    /// to become LSB-first. This method takes the raw .spdp string and reverses it
    /// before writing (producing LSB-first output).
    /// </summary>
    public void WriteBitsFromReversedString(string bits)
    {
        for (var i = bits.Length - 1; i >= 0; i--)
        {
            _bits.Add(bits[i] - '0');
        }
    }

    /// <summary>
    /// Encode a coordinate via CoordsHelper.EncodeServerCoordinate, then write
    /// the 4 result bytes as 32 bits LSB-first (bit 0 of byte 0 first).
    /// </summary>
    public void WriteCoordinate(double value)
    {
        var bytes = CoordsHelper.EncodeServerCoordinate(value);
        foreach (var b in bytes)
        {
            WriteBits(b, 8);
        }
    }

    /// <summary>
    /// Encode a string as Windows-1251 bytes, then write each byte's bits LSB-first.
    /// </summary>
    public void WriteStringBytes(string value)
    {
        var bytes = SphereGameServer.Win1251.GetBytes(value);
        foreach (var b in bytes)
        {
            WriteBits(b, 8);
        }
    }

    /// <summary>
    /// Convert the accumulated bits to a byte array.
    /// Bits are packed LSB-first: bit[0] goes to byte[0] bit 0, bit[7] goes to byte[0] bit 7, etc.
    /// </summary>
    public byte[] ToArray()
    {
        var byteCount = (_bits.Count + 7) / 8;
        var result = new byte[byteCount];

        for (var i = 0; i < _bits.Count; i++)
        {
            if (_bits[i] != 0)
            {
                result[i / 8] |= (byte)(1 << (i % 8));
            }
        }

        return result;
    }
}
