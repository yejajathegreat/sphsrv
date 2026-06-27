namespace SphereServer.Protocol;

/// <summary>
/// XOR decoding for client packets. Server packets are sent in plaintext.
/// Encoding starts at byte 9 (first 9 bytes are header, unencoded).
/// </summary>
public static class PacketCodec
{
    private static readonly byte[] EncodingMask = { 0x4B, 0x0D, 0xEF, 0x60, 0xC9, 0x9A, 0x70, 0x0E, 0x03 };

    public static byte[] DecodeClientPacket(byte[] input, int start = 9)
    {
        if (input.Length <= start)
            return input;

        var encoded = input.AsSpan(start);
        var result = new byte[input.Length];
        Array.Copy(input, result, start);

        byte mask3 = 0x0;
        for (int i = 0; i < encoded.Length; i++)
        {
            byte current = (byte)(encoded[i] ^ EncodingMask[i % 9] ^ mask3);
            result[i + start] = current;
            mask3 = (byte)(current * i + 2 * mask3);
        }

        return result;
    }
}
