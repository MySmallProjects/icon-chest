using System.Diagnostics;
using IconChest.Core.Common;
using Xunit;

namespace IconChest.Core.Tests;

public class WildcardMatcherTests
{
    [Theory]
    [InlineData("icon.png", "*.png", true)]
    [InlineData("icon.PNG", "*.png", true)]
    [InlineData("icon.jpg", "*.png", false)]
    [InlineData("image_01.jpeg", "image_??.jpeg", true)]
    [InlineData("image_001.jpeg", "image_??.jpeg", false)]
    [InlineData("test.svg", "test.svg", true)]
    [InlineData("prefix_abc_suffix.webp", "*abc*", true)]
    [InlineData("abc", "a?c", true)]
    [InlineData("", "", true)]
    [InlineData("any", "*", true)]
    [InlineData("something", "", false)]
    public void IsMatch_MatchesExpected(string text, string pattern, bool expected)
    {
        var result = WildcardMatcher.IsMatch(text, pattern, ignoreCase: true);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void HostilePattern_CompletesRapidlyWithoutCatastrophicBacktracking()
    {
        // Many consecutive stars against a non-matching string
        var hostilePattern = "**************************************************a";
        var longText = new string('b', 10000);

        var sw = Stopwatch.StartNew();
        var match = WildcardMatcher.IsMatch(longText, hostilePattern, ignoreCase: true);
        sw.Stop();

        Assert.False(match);
        // Must complete in under 50 milliseconds
        Assert.True(sw.ElapsedMilliseconds < 50, $"Elapsed was {sw.ElapsedMilliseconds}ms");
    }
}

