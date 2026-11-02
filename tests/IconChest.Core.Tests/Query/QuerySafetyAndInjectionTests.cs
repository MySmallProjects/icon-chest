using System.Diagnostics;
using IconChest.Core.Common;
using IconChest.Core.Query;
using Xunit;

namespace IconChest.Core.Tests.Query;

public class QuerySafetyAndInjectionTests
{
    [Fact]
    public void ByteIdenticalSql_QueriesDifferingOnlyInLiterals_ProduceIdenticalSql()
    {
        var q1 = QueryEngine.ParseAndCompile("width >= 100 and rating == 5 and title like \"apple*\"");
        var q2 = QueryEngine.ParseAndCompile("width >= 9999 and rating == 1 and title like \"banana*\"");

        Assert.True(q1.IsValid);
        Assert.True(q2.IsValid);
        Assert.NotNull(q1.Query);
        Assert.NotNull(q2.Query);

        // Byte-identical SQL
        Assert.Equal(q1.Query.WhereSql, q2.Query.WhereSql);

        // Parameters differ
        Assert.Equal(100L, q1.Query.Parameters["q0"]);
        Assert.Equal(9999L, q2.Query.Parameters["q0"]);
    }

    [Theory]
    [InlineData("title == \"' OR 1=1 --\"")]
    [InlineData("filename == \"'; DROP TABLE image; --\"")]
    [InlineData("tag == \"\\\" OR \\\"\\\"=\\\"\"")]
    [InlineData("description == \"-- comment\"")]
    [InlineData("folder under \"/home/user'; DELETE FROM image; --\"")]
    [InlineData("title contains \"100% and _ underscore\"")]
    public void HostileInputs_NeverAlterSqlStructure(string hostileQuery)
    {
        var comp = QueryEngine.ParseAndCompile(hostileQuery);
        Assert.True(comp.IsValid);
        Assert.NotNull(comp.Query);

        // SQL should never contain user SQL keywords or injected fragments outside parameterization
        Assert.DoesNotContain("DROP TABLE", comp.Query.WhereSql);
        Assert.DoesNotContain("DELETE FROM", comp.Query.WhereSql);
        Assert.DoesNotContain("1=1", comp.Query.WhereSql);
        Assert.DoesNotContain("--", comp.Query.WhereSql);
    }

    [Fact]
    public void PathologicalPattern_CompletesQuicklyWithoutReDoS()
    {
        // 50 stars against 1,000 characters
        var pattern = new string('*', 50) + "end";
        var text = new string('a', 1000) + "end";

        var sw = Stopwatch.StartNew();
        bool match = WildcardMatcher.IsMatchWithEscapes(text, pattern);
        sw.Stop();

        Assert.True(match);
        Assert.True(sw.ElapsedMilliseconds < 50, $"Wildcard match took too long: {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Limits_EnforcedReliably()
    {
        // 1. Query length > 2000
        var tooLong = new string('a', 2001);
        var res1 = QueryEngine.ParseAndCompile(tooLong);
        Assert.False(res1.IsValid);
        Assert.Equal(QueryErrorCodes.QueryTooLong, res1.Errors[0].Code);

        // 2. Pattern length > 200
        var longPattern = "title like \"" + new string('a', 205) + "\"";
        var res2 = QueryEngine.ParseAndCompile(longPattern);
        Assert.False(res2.IsValid);
        Assert.Equal(QueryErrorCodes.PatternTooLong, res2.Errors[0].Code);

        // 3. Too many values in IN list > 100
        var inValues = string.Join(", ", Enumerable.Range(1, 105).Select(i => $"v{i}"));
        var tooManyValues = $"tag in ({inValues})";
        var res3 = QueryEngine.ParseAndCompile(tooManyValues);
        Assert.False(res3.IsValid);
        Assert.Equal(QueryErrorCodes.TooManyValues, res3.Errors[0].Code);
    }
}

