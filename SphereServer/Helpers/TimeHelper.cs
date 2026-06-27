namespace SphereServer.Helpers;

/// <summary>
/// Sphere time: runs 12x faster than real time, epoch = 21.08.1998 10:00 UTC.
/// Encoded as 5 bytes.
/// </summary>
public static class TimeHelper
{
    private static readonly DateTime SphereEpoch = new(1998, 8, 21, 10, 0, 0, DateTimeKind.Utc);
    private const int TimeMultiplier = 12;

    public static byte[] EncodeCurrentSphereDateTime()
    {
        var realElapsed = DateTime.UtcNow - SphereEpoch;
        var sphereSeconds = (long)(realElapsed.TotalSeconds * TimeMultiplier);

        var result = new byte[5];
        for (int i = 0; i < 5; i++)
        {
            result[i] = (byte)(sphereSeconds & 0xFF);
            sphereSeconds >>= 8;
        }

        return result;
    }
}
