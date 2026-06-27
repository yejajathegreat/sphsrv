using System.Security.Cryptography;

namespace SphereServer.Auth;

/// <summary>
/// PBKDF2 password hashing compatible with SphereEmu format.
/// Salt: 16 bytes, Hash: 20 bytes, stored as base64(salt+hash).
/// </summary>
public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 20;
    private const int Iterations = 100;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);

        var combined = new byte[SaltSize + HashSize];
        Array.Copy(salt, 0, combined, 0, SaltSize);
        Array.Copy(hash, 0, combined, SaltSize, HashSize);

        return Convert.ToBase64String(combined);
    }

    public static bool Verify(string password, string storedHash)
    {
        var hashBytes = Convert.FromBase64String(storedHash);
        var salt = hashBytes[..SaltSize];
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);

        for (int i = 0; i < HashSize; i++)
        {
            if (hashBytes[i + SaltSize] != hash[i])
                return false;
        }

        return true;
    }
}
