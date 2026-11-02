using System.Security.Cryptography;

namespace IconChest.Desktop;

public static class SecretGenerator
{
    private const int SecretByteLength = 32;

    public static string GenerateSecret()
    {
        Span<byte> bytes = stackalloc byte[SecretByteLength];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}

