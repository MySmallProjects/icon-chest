using Xunit;

namespace IconChest.Desktop.Tests;

public class NavigationPolicyTests
{
    private readonly Uri _origin = new("http://127.0.0.1:5000");

    [Theory]
    [InlineData("http://127.0.0.1:5000/")]
    [InlineData("http://127.0.0.1:5000/index.html")]
    [InlineData("http://127.0.0.1:5000/api/folders?q=test")]
    [InlineData("http://127.0.0.1:5000/css/tokens.css")]
    public void IsAllowedNavigation_SameOrigin_ReturnsTrue(string url)
    {
        var policy = new NavigationPolicy(_origin);
        Assert.True(policy.IsAllowedNavigation(new Uri(url)));
    }

    [Theory]
    [InlineData("http://127.0.0.1:5001/")]
    [InlineData("http://localhost:5000/")]
    [InlineData("https://127.0.0.1:5000/")]
    [InlineData("https://google.com/")]
    [InlineData("file:///etc/passwd")]
    public void IsAllowedNavigation_DifferentOriginOrScheme_ReturnsFalse(string url)
    {
        var policy = new NavigationPolicy(_origin);
        Assert.False(policy.IsAllowedNavigation(new Uri(url)));
    }

    [Theory]
    [InlineData("https://example.com/docs")]
    [InlineData("http://otherhost:8080/path")]
    public void ShouldOpenInExternalBrowser_HttpOrHttpsDifferentOrigin_ReturnsTrue(string url)
    {
        var policy = new NavigationPolicy(_origin);
        Assert.True(policy.ShouldOpenInExternalBrowser(new Uri(url)));
    }

    [Theory]
    [InlineData("http://127.0.0.1:5000/")]
    [InlineData("file:///home/user/test.png")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,test")]
    public void ShouldOpenInExternalBrowser_SameOriginOrNonHttpSchemes_ReturnsFalse(string url)
    {
        var policy = new NavigationPolicy(_origin);
        Assert.False(policy.ShouldOpenInExternalBrowser(new Uri(url)));
    }

    [Fact]
    public void IsAllowedBridgeOrigin_MatchesApiOrigin()
    {
        var policy = new NavigationPolicy(_origin);
        Assert.True(policy.IsAllowedBridgeOrigin("http://127.0.0.1:5000"));
        Assert.True(policy.IsAllowedBridgeOrigin("http://127.0.0.1:5000/"));
        Assert.False(policy.IsAllowedBridgeOrigin("http://attacker.com"));
        Assert.False(policy.IsAllowedBridgeOrigin("http://127.0.0.1:9999"));
        Assert.False(policy.IsAllowedBridgeOrigin(null));
        Assert.False(policy.IsAllowedBridgeOrigin("not-a-url"));
    }
}

