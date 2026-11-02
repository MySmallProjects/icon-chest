using Xunit;

namespace IconChest.Desktop.Tests;

public class SecretGeneratorTests
{
    [Fact]
    public void GenerateSecret_ReturnsValidBase64UrlString()
    {
        var secret = SecretGenerator.GenerateSecret();

        Assert.NotNull(secret);
        // 32 bytes base64 without padding produces exactly 43 characters
        Assert.Equal(43, secret.Length);

        // URL safe characters only
        Assert.DoesNotContain("+", secret);
        Assert.DoesNotContain("/", secret);
        Assert.DoesNotContain("=", secret);
    }

    [Fact]
    public void GenerateSecret_IsCryptographicallyUnique()
    {
        var set = new HashSet<string>();
        for (int i = 0; i < 1000; i++)
        {
            var s = SecretGenerator.GenerateSecret();
            Assert.True(set.Add(s), $"Generated duplicate secret on iteration {i}");
        }
    }
}

