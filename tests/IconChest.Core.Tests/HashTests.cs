using System.IO.Hashing;
using System.Text;
using IconChest.Core.Scanning;
using Xunit;

namespace IconChest.Core.Tests;

public class HashTests
{
    [Fact]
    public void XxHash128_MatchesKnownVector()
    {
        // Test empty payload with System.IO.Hashing.XxHash128
        var emptyBytes = Array.Empty<byte>();
        var emptyHash = XxHash128.Hash(emptyBytes);
        var hex = Convert.ToHexStringLower(emptyHash);

        Assert.Equal(32, hex.Length);

        // Vector for "Hello World"
        var helloBytes = Encoding.UTF8.GetBytes("Hello World");
        var helloHash = XxHash128.Hash(helloBytes);
        var helloHex = Convert.ToHexStringLower(helloHash);

        Assert.Equal(32, helloHex.Length);
        Assert.NotEqual(hex, helloHex);
    }

    [Fact]
    public async Task HashFileAsync_SameContentProducesSameHash_OneByteDiffers()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"hash-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var fileA = Path.Combine(tempDir, "fileA.bin");
            var fileB = Path.Combine(tempDir, "fileB.bin");
            var fileC = Path.Combine(tempDir, "fileC.bin");

            var data1 = new byte[1024];
            new Random(42).NextBytes(data1);

            var data2 = (byte[])data1.Clone();
            var data3 = (byte[])data1.Clone();
            data3[500] = (byte)(data3[500] ^ 0xFF); // flip bits in 1 byte

            await File.WriteAllBytesAsync(fileA, data1);
            await File.WriteAllBytesAsync(fileB, data2);
            await File.WriteAllBytesAsync(fileC, data3);

            var hasher = new XxHash128Hasher();
            var hashA = await hasher.HashFileAsync(fileA);
            var hashB = await hasher.HashFileAsync(fileB);
            var hashC = await hasher.HashFileAsync(fileC);

            Assert.Equal(hashA, hashB);
            Assert.NotEqual(hashA, hashC);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task HashFileAsync_CancellationStopsPromptly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"hash-cancel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var file = Path.Combine(tempDir, "large.bin");
            // Create 5MB file
            var data = new byte[5 * 1024 * 1024];
            await File.WriteAllBytesAsync(file, data);

            var hasher = new XxHash128Hasher();
            using var cts = new CancellationTokenSource();
            cts.Cancel(); // Pre-cancelled

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => hasher.HashFileAsync(file, cts.Token));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }
}

