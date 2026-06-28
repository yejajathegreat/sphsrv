using System.Text;

namespace SphereServer.Helpers;

public static class ByteHelper
{
    // knelse naming: GetFirstByte = low byte, GetSecondByte = high byte
    public static byte GetFirstByte(ushort input) => (byte)(input & 0xFF);
    public static byte GetSecondByte(ushort input) => (byte)(input >> 8);

    // Our naming aliases
    public static byte MinorByte(ushort value) => GetFirstByte(value);
    public static byte MajorByte(ushort value) => GetSecondByte(value);

    public static ushort ByteSwap(ushort u) =>
        (ushort)(((u & 0b11111111) << 8) + ((u & 0b1111111100000000) >> 8));

    public static string ToHex(byte[] data) => BitConverter.ToString(data).Replace("-", " ");
    public static string ToHex(byte[] data, int offset, int count) =>
        BitConverter.ToString(data, offset, count).Replace("-", " ");

    public static string ToBinaryString(this byte b) =>
        Convert.ToString(b, 2).PadLeft(8, '0');

    public static string ToBinaryString(this ushort us) =>
        Convert.ToString(us, 2).PadLeft(16, '0');

    public static string ToBinaryString(this uint ui) =>
        Convert.ToString(ui, 2).PadLeft(32, '0');

    public static string ToBinaryString(this long l) =>
        Convert.ToString(l, 2).PadLeft(64, '0');

    public static string ByteArrayToBinaryString(byte[] ba, bool noPadding = false, bool addSpaces = false)
    {
        var hex = new StringBuilder(ba.Length * 2);
        foreach (var val in ba)
        {
            var str = Convert.ToString(val, 2);
            if (!noPadding) str = str.PadLeft(8, '0');
            hex.Append(str);
            if (addSpaces) hex.Append(' ');
        }
        return hex.ToString();
    }

    public static byte[] BinaryStringToByteArray(string s)
    {
        if (s.Length % 8 != 0) return Array.Empty<byte>();
        var result = new byte[s.Length / 8];
        for (var i = 0; i < s.Length; i += 8)
            result[i / 8] = Convert.ToByte(s[i..(i + 8)], 2);
        return result;
    }
}
