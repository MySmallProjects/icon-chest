using Xunit;

namespace IconChest.Desktop.Tests;

public class ReadinessLineParserTests
{
    [Theory]
    [InlineData("ICONCHEST_READY http://127.0.0.1:5000", "http://127.0.0.1:5000/")]
    [InlineData("  ICONCHEST_READY http://127.0.0.1:51234/  \r\n", "http://127.0.0.1:51234/")]
    [InlineData("ICONCHEST_READY https://localhost:8443", "https://localhost:8443/")]
    public void TryParse_ValidLine_ReturnsUri(string input, string expectedUri)
    {
        var uri = ReadinessLineParser.TryParse(input);
        Assert.NotNull(uri);
        Assert.Equal(new Uri(expectedUri), uri);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("info: Microsoft.Hosting.Lifetime[0] Application started.")]
    [InlineData("ICONCHEST_READY")]
    [InlineData("ICONCHEST_READY ")]
    [InlineData("ICONCHEST_READY not-a-valid-url")]
    [InlineData("ICONCHEST_READY ftp://127.0.0.1:21")]
    [InlineData("ICONCHEST_READY_EXTRA http://127.0.0.1:5000")]
    [InlineData("ICONCHEST_NOT_READY http://127.0.0.1:5000")]
    [InlineData("SOME_PREFIX ICONCHEST_READY http://127.0.0.1:5000")]
    public void TryParse_InvalidOrMalformedLine_ReturnsNull(string? input)
    {
        var uri = ReadinessLineParser.TryParse(input);
        Assert.Null(uri);
    }

    [Fact]
    public void TryParse_VeryLongLine_DoesNotThrowAndReturnsNull()
    {
        var longLine = "ICONCHEST_READY " + new string('x', 50000);
        var uri = ReadinessLineParser.TryParse(longLine);
        Assert.Null(uri);
    }
}

