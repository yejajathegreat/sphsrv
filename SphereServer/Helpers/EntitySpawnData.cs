using System.Text;

namespace SphereServer.Helpers;

/// <summary>
/// Entity spawn data, ported 1:1 from knelse DataModels/EntitySpawnData.cs
/// </summary>
public class EntitySpawnData
{
    public ushort ID;
    public ushort Unknown;
    public double X;
    public double Y;
    public double Z;
    public double Turn;
    public ushort HP;
    public ushort TypeID;
    public byte Level;

    public byte[] ToByteArray()
    {
        var sb = new StringBuilder();
        sb.Append("11111100"); //fc
        sb.Append("11010010"); //d2
        sb.Append("00111001"); //39
        sb.Append("01110000"); //70
        sb.Append("00000000"); //00
        sb.Append("11000000"); //c0
        var id_str = ID.ToBinaryString();
        sb.Append(id_str[13..]);
        sb.Append("01111");
        sb.Append(id_str[5..13]);
        var enttype_str = Unknown.ToBinaryString();
        sb.Append("000");
        sb.Append(id_str[..5]);
        sb.Append("01101001");
        sb.Append("11110000");
        var x_str = ByteHelper.ByteArrayToBinaryString(CoordsHelper.EncodeServerCoordinate(X));
        var y_str = ByteHelper.ByteArrayToBinaryString(CoordsHelper.EncodeServerCoordinate(Y));
        var z_str = ByteHelper.ByteArrayToBinaryString(CoordsHelper.EncodeServerCoordinate(Z));
        var t_str = ByteHelper.ByteArrayToBinaryString(CoordsHelper.EncodeServerCoordinate(Turn));

        sb.Append(x_str[2..8]);
        sb.Append("01");
        sb.Append(x_str[10..16]);
        sb.Append(x_str[..2]);
        sb.Append(x_str[18..24]);
        sb.Append(x_str[8..10]);
        sb.Append(x_str[26..32]);
        sb.Append(x_str[16..18]);
        sb.Append(y_str[2..8]);
        sb.Append(x_str[24..26]);

        sb.Append(y_str[10..16]);
        sb.Append(y_str[..2]);
        sb.Append(y_str[18..24]);
        sb.Append(y_str[8..10]);
        sb.Append(y_str[26..32]);
        sb.Append(y_str[16..18]);
        sb.Append(z_str[2..8]);
        sb.Append(y_str[24..26]);

        sb.Append(z_str[10..16]);
        sb.Append(z_str[..2]);
        sb.Append(z_str[18..24]);
        sb.Append(z_str[8..10]);
        sb.Append(z_str[26..32]);
        sb.Append(z_str[16..18]);
        sb.Append("101011");
        sb.Append(z_str[24..26]);
        sb.Append("01000110");
        sb.Append("11111100");
        sb.Append("10000000");
        sb.Append("11111000");
        sb.Append("00000001");
        var entid_str = TypeID.ToBinaryString();
        sb.Append(entid_str[9..]);
        sb.Append("1");
        sb.Append(entid_str[1..9]);
        var level_str = Level.ToBinaryString();
        sb.Append(level_str[2..]);
        sb.Append("01");
        sb.Append("111101");
        sb.Append(level_str[..2]);
        sb.Append("00000001");

        return ByteHelper.BinaryStringToByteArray(sb.ToString());
    }
}
