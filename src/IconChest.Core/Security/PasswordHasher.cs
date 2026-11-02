using System.Security.Cryptography;
using System.Text;

namespace IconChest.Core.Security;

public static class PasswordHasher
{
    public const int DefaultIterations = 600_000;
    public const int SaltByteLength = 16;
    public const int HashByteLength = 32;
    private const string CurrentVersion = "1";

    /// <summary>
    /// Hashes a password using PBKDF2-SHA256 with a random salt and at least 600,000 iterations.
    /// Format: $pbkdf2-sha256$v=1$i=600000$s=&lt;salt&gt;$h=&lt;hash&gt;
    /// </summary>
    public static string HashPassword(string password, int iterations = DefaultIterations)
    {
        if (password == null) throw new ArgumentNullException(nameof(password));
        if (iterations < DefaultIterations)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations), $"Iterations must be at least {DefaultIterations}.");
        }

        byte[] salt = RandomNumberGenerator.GetBytes(SaltByteLength);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            HashByteLength);

        string saltB64 = Convert.ToBase64String(salt);
        string hashB64 = Convert.ToBase64String(hash);

        return $"$pbkdf2-sha256$v={CurrentVersion}$i={iterations}$s={saltB64}$h={hashB64}";
    }

    /// <summary>
    /// Verifies a password against a versioned PBKDF2-SHA256 hash string in constant time.
    /// </summary>
    public static bool VerifyPassword(string password, string? hashString)
    {
        if (string.IsNullOrWhiteSpace(hashString) || password == null)
        {
            return false;
        }

        var parts = hashString.Split('$');
        // Expected format: ["", "pbkdf2-sha256", "v=1", "i=600000", "s=...", "h=..."]
        if (parts.Length != 6 || parts[1] != "pbkdf2-sha256")
        {
            return false;
        }

        string version = parts[2].StartsWith("v=", StringComparison.Ordinal) ? parts[2][2..] : string.Empty;
        if (version != "1")
        {
            return false;
        }

        if (!parts[3].StartsWith("i=", StringComparison.Ordinal) ||
            !int.TryParse(parts[3][2..], out int iterations) || iterations < 1)
        {
            return false;
        }

        if (!parts[4].StartsWith("s=", StringComparison.Ordinal) ||
            !parts[5].StartsWith("h=", StringComparison.Ordinal))
        {
            return false;
        }

        byte[] salt;
        byte[] expectedHash;
        try
        {
            salt = Convert.FromBase64String(parts[4][2..]);
            expectedHash = Convert.FromBase64String(parts[5][2..]);
        }
        catch (FormatException)
        {
            return false;
        }

        byte[] computedHash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            expectedHash.Length);

        return CryptographicOperations.FixedTimeEquals(computedHash, expectedHash);
    }

    /// <summary>
    /// Compares two secrets in constant time.
    /// </summary>
    public static bool VerifySecret(string? providedSecret, string? expectedSecret)
    {
        if (providedSecret == null || expectedSecret == null)
        {
            return false;
        }

        byte[] providedBytes = Encoding.UTF8.GetBytes(providedSecret);
        byte[] expectedBytes = Encoding.UTF8.GetBytes(expectedSecret);

        if (providedBytes.Length != expectedBytes.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }
}

