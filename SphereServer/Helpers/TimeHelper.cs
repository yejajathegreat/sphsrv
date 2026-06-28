namespace SphereServer.Helpers;

/// <summary>
/// Sphere time encoding, ported 1:1 from knelse TimeHelper.
/// Uses UnixTimeOrigin = 1649722100, multiplier = 12x.
/// </summary>
public static class TimeHelper
{
    public const int UnixTimeOrigin = 1649722100;

    public static DateTime GetCurrentSphereDateTime()
    {
        var now = new DateTimeOffset(DateTime.UtcNow);
        var fromUnixTimeOrigin = now.ToUnixTimeSeconds() - UnixTimeOrigin;
        var sphereTimeOffset = fromUnixTimeOrigin * 12;
        var sphereDateTime = new DateTime().AddSeconds(sphereTimeOffset);
        return sphereDateTime;
    }

    public static byte[] EncodeCurrentSphereDateTime()
    {
        var currentSphereTime = GetCurrentSphereDateTime();
        var minutes_last4 = (byte)((currentSphereTime.Minute & 0b1111) << 4);
        var firstDateByte = (byte)(minutes_last4 + 0b1000);
        var minutes_first2 = (byte)((currentSphereTime.Minute & 0b110000) >> 4);
        var hours = (byte)(currentSphereTime.Hour << 2);
        var days_last1 = (byte)((currentSphereTime.Day % 2) << 7);
        var secondDateByte = (byte)(days_last1 + hours + minutes_first2);
        var days_first4 = (byte)((currentSphereTime.Day & 0b11110) >> 1);
        var month = (byte)(currentSphereTime.Month << 4);
        var thirdDateByte = (byte)(month + days_first4);
        var years_last8 = (byte)(currentSphereTime.Year & 0b11111111);
        var years_first1 = (byte)((currentSphereTime.Year & 0b100000000) >> 8);
        var fourthDateByte = (byte)(0b00110100 + years_first1);

        return new[]
        {
            firstDateByte, secondDateByte,
            thirdDateByte, years_last8, fourthDateByte
        };
    }
}
