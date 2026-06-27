namespace SphereServer.Helpers;

public static class ByteHelper
{
    public static byte MinorByte(ushort value) => (byte)(value & 0xFF);
    public static byte MajorByte(ushort value) => (byte)(value >> 8);

    public static string ToHex(byte[] data) => BitConverter.ToString(data).Replace("-", " ");
    public static string ToHex(byte[] data, int offset, int count) =>
        BitConverter.ToString(data, offset, count).Replace("-", " ");
}
