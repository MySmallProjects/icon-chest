using System.IO.Hashing;

namespace IconChest.Core.Scanning;

public sealed class XxHash128Hasher : IFileHasher
{
    public const int BufferSizeBytes = 80 * 1024; // 80 KB
    public const string Algorithm = "xxh128";

    public string AlgorithmName => Algorithm;

    public async Task<string> HashFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var hasher = new XxHash128();
        var buffer = new byte[BufferSizeBytes];

        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSizeBytes,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
            {
                break;
            }

            hasher.Append(buffer.AsSpan(0, read));
        }

        var hashBytes = hasher.GetCurrentHash();
        return Convert.ToHexStringLower(hashBytes);
    }
}

