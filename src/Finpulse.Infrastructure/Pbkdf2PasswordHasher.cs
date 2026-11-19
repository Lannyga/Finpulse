using System.Globalization;
using System.Security.Cryptography;
using Finpulse.Application;

namespace Finpulse.Infrastructure;

/// <summary>Almacena formato PBKDF2-SHA256$iteraciones$sal$derivado; nunca texto plano.</summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int Iterations = 600_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const string Prefix = "PBKDF2-SHA256";

    public string HashPassword(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations,
            HashAlgorithmName.SHA256, HashBytes);
        return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool VerifyPassword(string password, string storedHash)
    {
        try
        {
            var parts = storedHash.Split('$');
            if (parts.Length != 4 || parts[0] != Prefix ||
                !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var n) ||
                n < 100_000 || n > 1_200_000)
                return false;
            byte[] salt = Convert.FromBase64String(parts[2]);
            byte[] expected = Convert.FromBase64String(parts[3]);
            if (salt.Length != SaltBytes || expected.Length != HashBytes) return false;
            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, n,
                HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }
}
