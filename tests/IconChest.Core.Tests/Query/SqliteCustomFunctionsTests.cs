using Dapper;
using IconChest.Core.Common;
using IconChest.Core.Data;
using Xunit;

namespace IconChest.Core.Tests.Query;

public class SqliteCustomFunctionsTests
{
    [Theory]
    [InlineData("test", "test", true)]
    [InlineData("TEST", "test", true)]
    [InlineData("Éléphant", "éléphant", true)]
    [InlineData("café", "CAFÉ", true)]
    [InlineData("naïve", "NAÏVE", true)]
    [InlineData("apple", "banana", false)]
    [InlineData(null, null, true)]
    [InlineData("a", null, false)]
    public void IqEq_MatchesCSharpReference(string? a, string? b, bool expected)
    {
        var factory = new DatabaseConnectionFactory(":memory:");
        using var conn = factory.CreateAndOpenConnection();

        bool result = conn.ExecuteScalar<bool>("SELECT iq_eq(@a, @b);", new { a, b });
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("icon.png", "*.png", true)]
    [InlineData("icon.png", "icon.*", true)]
    [InlineData("ICON.PNG", "*.png", true)]
    [InlineData("elephant.svg", "*ph*", true)]
    [InlineData("ÉLÉPHANT", "*léph*", true)]
    [InlineData("file*star.txt", @"file\*star.txt", true)]
    [InlineData("file?question.txt", @"file\?question.txt", true)]
    [InlineData("icon.png", "*.jpg", false)]
    [InlineData("apple", "a?p?e", true)]
    public void IqLike_MatchesCSharpReference(string text, string pattern, bool expected)
    {
        var factory = new DatabaseConnectionFactory(":memory:");
        using var conn = factory.CreateAndOpenConnection();

        bool result = conn.ExecuteScalar<bool>("SELECT iq_like(@text, @pattern);", new { text, pattern });
        Assert.Equal(expected, result);

        bool csRef = WildcardMatcher.IsMatchWithEscapes(text, pattern);
        Assert.Equal(csRef, result);
    }
}

