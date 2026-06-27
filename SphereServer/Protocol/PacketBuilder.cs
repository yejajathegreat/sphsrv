using SphereServer.Helpers;

namespace SphereServer.Protocol;

/// <summary>
/// Builds server packets with standard header: [length LE][0x2C 0x01][padding][content]
/// </summary>
public static class PacketBuilder
{
    private const ushort ValidationCode = 0x2C01;

    public static byte[] Build(byte[]? content = null, int padZeros = 2)
    {
        if (content is null)
            return CommonPackets.TransmissionEnd;

        ushort packetSize = (ushort)(content.Length + 4 + padZeros);
        var result = new byte[packetSize];

        result[0] = ByteHelper.MinorByte(packetSize);
        result[1] = ByteHelper.MajorByte(packetSize);
        result[2] = ByteHelper.MajorByte(ValidationCode); // 0x2C
        result[3] = ByteHelper.MinorByte(ValidationCode); // 0x01

        // padZeros bytes are already 0
        content.CopyTo(result, 4 + padZeros);

        return result;
    }
}
