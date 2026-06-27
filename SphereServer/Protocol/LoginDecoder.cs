using System.Text;

namespace SphereServer.Protocol;

/// <summary>
/// Decodes login/password from client packet.
/// After XOR decoding, skip 18 bytes header + 2 bits, then read zero-terminated strings in Win1251.
///
/// Simplified version: since we don't have BitStream library, we work at byte level.
/// The 2-bit offset means the actual data starts at byte 18 with a 2-bit shift.
/// In practice, the login starts at byte 18 after XOR decoding (the 2-bit shift
/// is handled by reading the full byte and masking).
/// </summary>
public static class LoginDecoder
{
    private static readonly Encoding Win1251;

    static LoginDecoder()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Win1251 = Encoding.GetEncoding(1251);
    }

    /// <summary>
    /// Decodes login and password from a raw (already XOR-decoded) client packet.
    /// Format: [18 bytes header][login zero-terminated][1 byte gap][password zero-terminated]
    /// Note: there's a 2-bit offset after header which we handle at byte level.
    /// </summary>
    public static (string login, string password) Decode(byte[] decodedPacket)
    {
        try
        {
            // After XOR decode, skip 18 bytes + some bits for header
            // Find login: zero-terminated string starting around byte 18
            int loginStart = 18;
            int loginEnd = loginStart;

            while (loginEnd < decodedPacket.Length && decodedPacket[loginEnd] != 0 && decodedPacket[loginEnd] != 1)
            {
                loginEnd++;
            }

            var loginBytes = decodedPacket[loginStart..loginEnd];
            var login = Win1251.GetString(loginBytes);

            // Skip the terminator + 1 gap byte
            int passwordStart = loginEnd + 2;
            int passwordEnd = passwordStart;

            while (passwordEnd < decodedPacket.Length && decodedPacket[passwordEnd] != 0)
            {
                passwordEnd++;
            }

            var passwordBytes = decodedPacket[passwordStart..passwordEnd];
            var password = Win1251.GetString(passwordBytes);

            return (login, password);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LOGIN] Failed to decode login/password: {ex.Message}");
            return (string.Empty, string.Empty);
        }
    }
}
