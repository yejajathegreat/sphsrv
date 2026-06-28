namespace SphereServer.Protocol;

/// <summary>
/// Decodes login/password from client packet.
/// Ported 1:1 from knelse LoginHelper.GetLoginAndPassword.
/// Character-level encoding (NOT XOR):
/// - Login starts at byte 18 of the RAW packet
/// - First login byte is adjusted by -3
/// - First password byte is adjusted by +1
/// - Even byte: letter = (byte / 4 - 1 + 'A')
/// - Odd byte: digit = (byte / 4 - 48 + '0')
/// </summary>
public static class LoginDecoder
{
    public static (string login, string password) Decode(byte[] rcvBuffer)
    {
        try
        {
            var loginEnd = 18;

            for (; loginEnd < rcvBuffer.Length; loginEnd++)
            {
                if (rcvBuffer[loginEnd] == 0 || rcvBuffer[loginEnd] == 1)
                    break;
            }

            var login = rcvBuffer[18..loginEnd];
            var passwordEnd = loginEnd + 1;

            for (; passwordEnd < rcvBuffer.Length; passwordEnd++)
            {
                if (rcvBuffer[passwordEnd] == 0)
                    break;
            }

            var password = rcvBuffer[(loginEnd + 1)..passwordEnd];

            var loginDecode = new char[login.Length];
            login[0] -= 3;

            for (var i = 0; i < login.Length; i++)
            {
                if (login[i] % 2 == 0)
                    loginDecode[i] = (char)(login[i] / 4 - 1 + 'A');
                else
                    loginDecode[i] = (char)(login[i] / 4 - 48 + '0');
            }

            var passwordDecode = new char[password.Length];
            password[0] += 1;

            for (var i = 0; i < password.Length; i++)
            {
                if (password[i] % 2 == 0)
                    passwordDecode[i] = (char)(password[i] / 4 - 1 + 'A');
                else
                    passwordDecode[i] = (char)(password[i] / 4 - 48 + '0');
            }

            var loginStr = new string(loginDecode);
            var passwordStr = new string(passwordDecode);

            Console.WriteLine($"[LOGIN] Decoded: [{loginStr}] / [{passwordStr}]");
            return (loginStr, passwordStr);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LOGIN] Decode error: {ex.Message}");
            return (string.Empty, string.Empty);
        }
    }
}
