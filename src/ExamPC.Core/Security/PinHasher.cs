using System.Security.Cryptography;

namespace ExamPC.Core.Security;

/// <summary>
/// Hash PIN pengawas dengan PBKDF2-SHA256. PIN polos tidak pernah menyentuh disk,
/// jadi siswa yang membongkar file konfigurasi tetap tidak bisa keluar dari ujian.
/// </summary>
public static class PinHasher
{
    private const int SaltBytes = 16;
    private const int KeyBytes = 32;
    private const int Iterations = 210_000; // rekomendasi OWASP 2023 untuk PBKDF2-SHA256

    public static string Hash(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var key = Rfc2898DeriveBytes.Pbkdf2(pin, salt, Iterations, HashAlgorithmName.SHA256, KeyBytes);
        return $"pbkdf2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public static bool Verify(string pin, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2") return false;
        if (!int.TryParse(parts[1], out var iterations)) return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(pin, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
