using System.Text;

namespace SphereServer.Protocol;

/// <summary>
/// Decodes login/password from client packet after XOR decoding.
/// The original SphereEmu uses BitStream to skip 18 bytes + 2 bits,
/// then reads zero-terminated strings in Win1251.
/// We implement the bit-level reading without BitStream library.
/// </summary>
public static class LoginDecoder
{
    private static readonly Encoding Win1251;

    static LoginDecoder()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Win1251 = Encoding.GetEncoding(1251);
    }

    public static (string login, string password) Decode(byte[] decodedPacket)
    {
        try
        {
            Console.WriteLine($"[LOGIN] Packet length: {decodedPacket.Length}");
            Console.WriteLine($"[LOGIN] Hex: {BitConverter.ToString(decodedPacket).Replace("-", " ")}");

            if (decodedPacket.Length <= 18)
            {
                Console.WriteLine("[LOGIN] Packet too short");
                return (string.Empty, string.Empty);
            }

            // Method 1: BitStream approach (skip 18 bytes + 2 bits)
            // Reading bytes with 2-bit offset means each byte is:
            // (current >> 2) | ((next & 0x03) << 6)
            int bitOffset = 18 * 8 + 2; // 18 bytes + 2 bits

            var login = ReadZeroTerminatedString(decodedPacket, ref bitOffset);
            Console.WriteLine($"[LOGIN] Login (bitstream): [{login}]");

            // Skip 1 byte (8 bits)
            bitOffset += 8;

            var password = ReadZeroTerminatedString(decodedPacket, ref bitOffset);
            Console.WriteLine($"[LOGIN] Password (bitstream): [{password}]");

            if (!string.IsNullOrEmpty(login))
                return (login, password);

            // Method 2: Fallback - try plain byte scan from offset 18
            return DecodeFallback(decodedPacket);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LOGIN] Decode error: {ex.Message}");
            return DecodeFallback(decodedPacket);
        }
    }

    /// <summary>
    /// Read a zero-terminated string from bit-offset position.
    /// Each character is 8 bits read from the given bit position.
    /// </summary>
    private static string ReadZeroTerminatedString(byte[] data, ref int bitOffset)
    {
        var bytes = new List<byte>();

        while (bitOffset + 8 <= data.Length * 8)
        {
            byte b = ReadByte(data, bitOffset);
            if (b == 0 || b == 1) // zero terminator (0x00 or 0x01)
            {
                bitOffset += 8;
                break;
            }
            bytes.Add(b);
            bitOffset += 8;

            if (bytes.Count > 64) break; // safety limit
        }

        return bytes.Count > 0 ? Win1251.GetString(bytes.ToArray()) : string.Empty;
    }

    /// <summary>
    /// Read 8 bits from arbitrary bit position in byte array.
    /// </summary>
    private static byte ReadByte(byte[] data, int bitOffset)
    {
        int byteIndex = bitOffset / 8;
        int bitShift = bitOffset % 8;

        if (byteIndex >= data.Length) return 0;

        if (bitShift == 0)
            return data[byteIndex];

        // Read across byte boundary
        int result = data[byteIndex] >> bitShift;
        if (byteIndex + 1 < data.Length)
            result |= (data[byteIndex + 1] << (8 - bitShift));

        return (byte)(result & 0xFF);
    }

    /// <summary>
    /// Fallback: scan for printable strings in the packet.
    /// </summary>
    private static (string login, string password) DecodeFallback(byte[] decoded)
    {
        try
        {
            // Try scanning from various offsets
            for (int start = 16; start < Math.Min(24, decoded.Length); start++)
            {
                var login = ExtractString(decoded, start);
                if (login.Length >= 2)
                {
                    int passStart = start + login.Length + 1;
                    // Try +1 and +2 gap
                    for (int gap = 1; gap <= 3; gap++)
                    {
                        var password = ExtractString(decoded, passStart + gap - 1);
                        if (password.Length >= 1)
                        {
                            Console.WriteLine($"[LOGIN] Fallback found at offset {start}+{gap}: [{login}] / [{password}]");
                            return (login, password);
                        }
                    }
                }
            }
        }
        catch { }

        Console.WriteLine("[LOGIN] All decode methods failed");
        return (string.Empty, string.Empty);
    }

    private static string ExtractString(byte[] data, int start)
    {
        if (start >= data.Length) return string.Empty;
        var bytes = new List<byte>();
        for (int i = start; i < data.Length; i++)
        {
            if (data[i] == 0 || data[i] == 1) break;
            if (data[i] < 0x20 && data[i] != 0x0A && data[i] != 0x0D) break;
            bytes.Add(data[i]);
            if (bytes.Count > 64) break;
        }
        return bytes.Count > 0 ? Win1251.GetString(bytes.ToArray()) : string.Empty;
    }
}
