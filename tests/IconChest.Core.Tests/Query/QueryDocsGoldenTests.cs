using System.Text;
using IconChest.Core.Query;
using Xunit;

namespace IconChest.Core.Tests.Query;

public class QueryDocsGoldenTests
{
    [Fact]
    public void Docs_MarkdownGoldenTest_MatchesCurrentCodeDefinition()
    {
        string expectedMarkdown = QueryHelpGenerator.GenerateMarkdownDocumentation();

        // Find docs/QUERY-LANGUAGE.md
        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        string docPath = Path.Combine(repoRoot, "docs", "QUERY-LANGUAGE.md");

        if (!File.Exists(docPath))
        {
            // Fallback
            docPath = Path.Combine(Directory.GetCurrentDirectory(), "docs", "QUERY-LANGUAGE.md");
        }

        Assert.True(File.Exists(docPath), $"Documentation file not found at: {docPath}");
        string actualMarkdown = File.ReadAllText(docPath, Encoding.UTF8);

        // Normalize newlines
        string normExpected = expectedMarkdown.Replace("\r\n", "\n").Trim();
        string normActual = actualMarkdown.Replace("\r\n", "\n").Trim();

        Assert.Equal(normExpected, normActual);
    }

    [Fact]
    public void HelpModel_ContainsAllFieldsAndErrors()
    {
        var help = QueryHelpGenerator.GetHelpModel();

        Assert.Equal(FieldRegistry.Fields.Count, help.Fields.Count);
        Assert.Equal(QueryHelpGenerator.ErrorDefinitions.Count, help.ErrorCodes.Count);
        Assert.Equal(QueryExamples.All.Count, help.Examples.Count);

        // Verify known error codes are present
        var errorCodes = help.ErrorCodes.Select(e => e.Code).ToHashSet();
        Assert.Contains(QueryErrorCodes.QueryTooLong, errorCodes);
        Assert.Contains(QueryErrorCodes.UnexpectedCharacter, errorCodes);
        Assert.Contains(QueryErrorCodes.UnterminatedString, errorCodes);
        Assert.Contains(QueryErrorCodes.MissingConnective, errorCodes);
        Assert.Contains(QueryErrorCodes.UnknownField, errorCodes);
        Assert.Contains(QueryErrorCodes.WildcardNotAllowed, errorCodes);
        Assert.Contains(QueryErrorCodes.InvalidDate, errorCodes);
        Assert.Contains(QueryErrorCodes.InvalidSize, errorCodes);
    }
}

