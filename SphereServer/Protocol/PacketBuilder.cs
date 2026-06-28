using SphereServer.Helpers;

namespace SphereServer.Protocol;

/// <summary>
/// Builds server packets with standard header: [length LE][0x2C 0x01][padding][content]
/// Ported 1:1 from knelse Packet.cs
/// </summary>
public static class PacketBuilder
{
    private static readonly ushort PacketValidationCodeOK = 0x2C01;
    private static readonly byte[] EmptyPacketByteArray = { 0x04, 0x00, 0xF4, 0x01 };

    public static byte[] Build(byte[]? content = null, int padZeros = 2)
    {
        if (content is null)
            return EmptyPacketByteArray;

        var packetSize = (ushort)(content.Length + 4 + padZeros);
        var result = new byte[content.Length + 4 + padZeros];

        result[0] = ByteHelper.GetFirstByte(packetSize);
        result[1] = ByteHelper.GetSecondByte(packetSize);
        result[2] = ByteHelper.GetSecondByte(PacketValidationCodeOK);
        result[3] = ByteHelper.GetFirstByte(PacketValidationCodeOK);

        for (var i = 0; i < padZeros; i++)
            result[4 + i] = 0x00;

        content.CopyTo(result, 4 + padZeros);
        return result;
    }
}
