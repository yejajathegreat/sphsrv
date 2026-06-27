namespace SphereServer.Helpers;

public class WorldCoords
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double Turn { get; set; }

    public WorldCoords(double x = 0, double y = 0, double z = 0, double turn = 0)
    {
        X = x; Y = y; Z = z; Turn = turn;
    }

    public override string ToString() => $"({X:F1}, {Y:F1}, {Z:F1}, {Turn:F2})";
}

/// <summary>
/// Coordinate encoding/decoding for Sphere protocol.
/// Server coords: custom float format (1 sign + 7 scale + 1 odd flag + 23 mantissa).
/// Client coords: IEEE-like with bit shifts.
/// Ported from SphereEmu CoordsHelper.cs
/// </summary>
public static class CoordsHelper
{
    public static byte[] EncodeServerCoordinate(double a)
    {
        int scale = 69;
        double aAbs = Math.Abs(a);
        double aTemp = aAbs;
        int steps = 0;

        if ((int)aAbs == 0)
        {
            scale = 58;
        }
        else if (aTemp < 2048)
        {
            while (aTemp < 2048)
            {
                aTemp *= 2;
                steps++;
            }
            scale -= (steps + 1) / 2;
            if (scale < 0) scale = 58;
        }
        else
        {
            while (aTemp > 4096)
            {
                aTemp /= 2;
                steps++;
            }
            scale += steps / 2;
        }

        byte a3 = (byte)(((a < 0 ? 1 : 0) << 7) + scale);
        double mul = Math.Pow(2, (int)Math.Log(aAbs, 2));
        int numToEncode = (int)(0b100000000000000000000000 * (aAbs / mul + 1));

        byte a2 = (byte)(((numToEncode & 0b111111110000000000000000) >> 16) + (steps % 2 == 1 ? 0b10000000 : 0));
        byte a1 = (byte)((numToEncode & 0b1111111100000000) >> 8);
        byte a0 = (byte)(numToEncode & 0b11111111);

        return [a0, a1, a2, a3];
    }

    public static double DecodeClientCoordinate(byte[] a)
    {
        int xScale = ((a[4] & 0b11111) << 3) + ((a[3] & 0b11100000) >> 5);
        if (xScale == 126) return 0.0;

        double baseCoord = Math.Pow(2, xScale - 127);
        int sign = (a[4] & 0b100000) > 0 ? -1 : 1;

        return (1 + (float)(((a[3] & 0b11111) << 18) + (a[2] << 10) + (a[1] << 2) +
                            ((a[0] & 0b11000000) >> 6)) / 0b100000000000000000000000) * baseCoord * sign;
    }

    /// <summary>
    /// Extract player coords from ping packet (0x26, 38 bytes).
    /// X at offset 21, Y at 25, Z at 29, Turn at 33 — each 5 bytes.
    /// </summary>
    public static WorldCoords GetCoordsFromPingBytes(byte[] rcvBuffer)
    {
        var x = DecodeClientCoordinate(rcvBuffer.AsSpan(21, 5).ToArray());
        var y = DecodeClientCoordinate(rcvBuffer.AsSpan(25, 5).ToArray());
        var z = DecodeClientCoordinate(rcvBuffer.AsSpan(29, 5).ToArray());
        var turn = DecodeClientCoordinate(rcvBuffer.AsSpan(33, 5).ToArray());

        return new WorldCoords(x, y, z, turn);
    }
}
