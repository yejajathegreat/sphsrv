using System.Security.Cryptography;

namespace SphereServer.Auth;

/// <summary>
/// PBKDF2 password hashing, ported 1:1 from knelse LoginHelper.
/// Salt: 16 bytes, Hash: 20 bytes, 100000 iterations, stored as base64(salt+hash).
/// </summary>
public static class PasswordHasher
{
    public static string Hash(string password)
    {
        var salt = new byte[16];
        RandomNumberGenerator.Fill(salt);
        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 100000);
        var hash = pbkdf2.GetBytes(20);
        var saltedHash = new byte[36];
        Array.Copy(salt, 0, saltedHash, 0, 16);
        Array.Copy(hash, 0, saltedHash, 16, 20);
        return Convert.ToBase64String(saltedHash);
    }

    public static bool Verify(string password, string storedHash)
    {
        var hashBytes = Convert.FromBase64String(storedHash);
        var salt = new byte[16];
        Array.Copy(hashBytes, 0, salt, 0, 16);
        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 100000);
        var hash = pbkdf2.GetBytes(20);

        for (var i = 0; i < 20; i++)
        {
            if (hashBytes[i + 16] != hash[i])
                return false;
        }

        return true;
    }
}
