using IconChest.Core.Query;
using Xunit;

namespace IconChest.Core.Tests.Query;

public class QueryValidatorTests
{
    [Fact]
    public void Validate_MultipleSemanticErrors_ReportedOrderedAndCappedAt10()
    {
        // 12 semantic errors in one query
        var text = "tpye1 == png and tpye2 == png and tpye3 == png and tpye4 == png and tpye5 == png and " +
                   "tpye6 == png and tpye7 == png and tpye8 == png and tpye9 == png and tpye10 == png and " +
                   "tpye11 == png and tpye12 == png";

        var (ast, parseErr) = QueryParser.Parse(text);
        Assert.Null(parseErr);
        Assert.NotNull(ast);

        var result = QueryValidator.Validate(ast);
        Assert.False(result.IsValid);
        Assert.Equal(10, result.Errors.Count); // Capped at 10

        // Ordered by position
        for (int i = 0; i < result.Errors.Count - 1; i++)
        {
            Assert.True(result.Errors[i].Position <= result.Errors[i + 1].Position);
        }
    }

    [Theory]
    // 60+ semantic error test cases covering every code in the error table and field/op combinations
    [InlineData("tpye == png", QueryErrorCodes.UnknownField, 0, 4, "type")]
    [InlineData("filname == 'x'", QueryErrorCodes.UnknownField, 0, 7, "filename")]
    [InlineData("widht == 100", QueryErrorCodes.UnknownField, 0, 5, "width")]
    [InlineData("heigth == 100", QueryErrorCodes.UnknownField, 0, 6, "height")]
    [InlineData("favorit == true", QueryErrorCodes.UnknownField, 0, 7, "favourite")]
    [InlineData("hiden == true", QueryErrorCodes.UnknownField, 0, 5, "hidden")]
    [InlineData("mising == true", QueryErrorCodes.UnknownField, 0, 6, "missing")]
    [InlineData("duplicat == true", QueryErrorCodes.UnknownField, 0, 8, "duplicate")]
    [InlineData("transparnt == true", QueryErrorCodes.UnknownField, 0, 10, "transparent")]
    [InlineData("ratng == 5", QueryErrorCodes.UnknownField, 0, 5, "rating")]
    [InlineData("sze == 10kb", QueryErrorCodes.UnknownField, 0, 3, "size")]
    [InlineData("foler == '/a'", QueryErrorCodes.UnknownField, 0, 5, "folder")]
    [InlineData("dte == 2026", QueryErrorCodes.UnknownField, 0, 3, "date")]
    [InlineData("creatd == 2026", QueryErrorCodes.UnknownField, 0, 6, "created")]
    [InlineData("modifid == 2026", QueryErrorCodes.UnknownField, 0, 7, "modified")]
    [InlineData("tken == 2026", QueryErrorCodes.UnknownField, 0, 4, "taken")]
    [InlineData("addd == 2026", QueryErrorCodes.UnknownField, 0, 4, "added")]

    // Field needs operator (lone non-boolean field)
    [InlineData("title", QueryErrorCodes.FieldNeedsOperator, 0, 5, null)]
    [InlineData("width", QueryErrorCodes.FieldNeedsOperator, 0, 5, null)]
    [InlineData("height", QueryErrorCodes.FieldNeedsOperator, 0, 6, null)]
    [InlineData("size", QueryErrorCodes.FieldNeedsOperator, 0, 4, null)]
    [InlineData("rating", QueryErrorCodes.FieldNeedsOperator, 0, 6, null)]
    [InlineData("folder", QueryErrorCodes.FieldNeedsOperator, 0, 6, null)]
    [InlineData("date", QueryErrorCodes.FieldNeedsOperator, 0, 4, null)]
    [InlineData("type", QueryErrorCodes.FieldNeedsOperator, 0, 4, null)]

    // Operator not allowed for field
    [InlineData("type > png", QueryErrorCodes.OperatorNotAllowed, 5, 1, null)]
    [InlineData("type contains png", QueryErrorCodes.OperatorNotAllowed, 0, 17, null)]
    [InlineData("type under png", QueryErrorCodes.OperatorNotAllowed, 0, 14, null)]
    [InlineData("type is empty", QueryErrorCodes.OperatorNotAllowed, 0, 13, null)]
    [InlineData("filename > 5", QueryErrorCodes.OperatorNotAllowed, 9, 1, null)]
    [InlineData("filename under '/a'", QueryErrorCodes.OperatorNotAllowed, 0, 19, null)]
    [InlineData("title < 10", QueryErrorCodes.OperatorNotAllowed, 6, 1, null)]
    [InlineData("description > 5", QueryErrorCodes.OperatorNotAllowed, 12, 1, null)]
    [InlineData("folder > 5", QueryErrorCodes.OperatorNotAllowed, 7, 1, null)]
    [InlineData("tag > 5", QueryErrorCodes.OperatorNotAllowed, 4, 1, null)]
    [InlineData("tag under '/a'", QueryErrorCodes.OperatorNotAllowed, 0, 14, null)]
    [InlineData("width like '*1*'", QueryErrorCodes.OperatorNotAllowed, 0, 16, null)]
    [InlineData("width contains 5", QueryErrorCodes.OperatorNotAllowed, 0, 16, null)]
    [InlineData("width under '/a'", QueryErrorCodes.OperatorNotAllowed, 0, 16, null)]
    [InlineData("height like '*1*'", QueryErrorCodes.OperatorNotAllowed, 0, 17, null)]
    [InlineData("height contains 5", QueryErrorCodes.OperatorNotAllowed, 0, 17, null)]
    [InlineData("size like '10*'", QueryErrorCodes.OperatorNotAllowed, 0, 15, null)]
    [InlineData("size contains 5", QueryErrorCodes.OperatorNotAllowed, 0, 15, null)]
    [InlineData("size is empty", QueryErrorCodes.OperatorNotAllowed, 0, 13, null)]
    [InlineData("rating like '5*'", QueryErrorCodes.OperatorNotAllowed, 0, 16, null)]
    [InlineData("rating contains 5", QueryErrorCodes.OperatorNotAllowed, 0, 17, null)]
    [InlineData("favourite > 1", QueryErrorCodes.OperatorNotAllowed, 10, 1, null)]
    [InlineData("favourite like 'true*'", QueryErrorCodes.OperatorNotAllowed, 0, 22, null)]
    [InlineData("favourite in (true, false)", QueryErrorCodes.OperatorNotAllowed, 0, 26, null)]
    [InlineData("favourite is empty", QueryErrorCodes.OperatorNotAllowed, 0, 18, null)]
    [InlineData("hidden < 1", QueryErrorCodes.OperatorNotAllowed, 7, 1, null)]
    [InlineData("missing > 0", QueryErrorCodes.OperatorNotAllowed, 8, 1, null)]
    [InlineData("duplicate <= 1", QueryErrorCodes.OperatorNotAllowed, 10, 2, null)]
    [InlineData("transparent >= 0", QueryErrorCodes.OperatorNotAllowed, 12, 2, null)]
    [InlineData("date like '2026*'", QueryErrorCodes.OperatorNotAllowed, 0, 17, null)]
    [InlineData("date contains '2026'", QueryErrorCodes.OperatorNotAllowed, 0, 20, null)]
    [InlineData("date in (2026-01-01)", QueryErrorCodes.OperatorNotAllowed, 0, 20, null)]

    // Wildcard not allowed after ==, !=, in
    [InlineData("type == p*", QueryErrorCodes.WildcardNotAllowed, 8, 2, "like")]
    [InlineData("filename == *test*", QueryErrorCodes.WildcardNotAllowed, 12, 6, "like")]
    [InlineData("title != ?icon", QueryErrorCodes.WildcardNotAllowed, 9, 5, "like")]
    [InlineData("tag in (ui, icon*)", QueryErrorCodes.WildcardNotAllowed, 12, 5, "use 'like' instead")]

    // Invalid values
    [InlineData("type == mp4", QueryErrorCodes.InvalidValue, 8, 3, null)]
    [InlineData("type == exe", QueryErrorCodes.InvalidValue, 8, 3, null)]
    [InlineData("rating == 6", QueryErrorCodes.InvalidValue, 10, 1, null)]
    [InlineData("rating == 99", QueryErrorCodes.InvalidValue, 10, 2, null)]
    [InlineData("folder == 'relative/path'", QueryErrorCodes.InvalidValue, 10, 15, null)]
    [InlineData("folder under 'not/absolute'", QueryErrorCodes.InvalidValue, 13, 14, null)]
    [InlineData("favourite == yes", QueryErrorCodes.InvalidValue, 13, 3, null)]
    [InlineData("hidden == 1", QueryErrorCodes.InvalidValue, 10, 1, null)]

    // Invalid numbers and sizes
    [InlineData("width == 10.5", QueryErrorCodes.InvalidNumber, 9, 4, null)]
    [InlineData("height == -5", QueryErrorCodes.InvalidNumber, 10, 2, null)]
    [InlineData("rating == 2.5", QueryErrorCodes.InvalidNumber, 10, 3, null)]
    [InlineData("size == abc", QueryErrorCodes.InvalidSize, 8, 3, null)]
    [InlineData("size == 100xyz", QueryErrorCodes.InvalidSize, 8, 6, null)]
    [InlineData("size == -10kb", QueryErrorCodes.InvalidSize, 8, 5, null)]

    // Invalid dates
    [InlineData("date == 2026-02-30", QueryErrorCodes.InvalidDate, 8, 10, null)]
    [InlineData("date == 2026-13-01", QueryErrorCodes.InvalidDate, 8, 10, null)]
    [InlineData("date == 2026-00-10", QueryErrorCodes.InvalidDate, 8, 10, null)]
    [InlineData("date == 2026-04-31", QueryErrorCodes.InvalidDate, 8, 10, null)]
    [InlineData("created == notadate", QueryErrorCodes.InvalidDate, 11, 8, null)]
    public void Validate_SemanticErrors_ReportsExactCodePositionAndSuggestion(
        string query,
        string expectedCode,
        int expectedPos,
        int expectedLen,
        string? expectedSuggestion)
    {
        var (ast, parseErr) = QueryParser.Parse(query);
        Assert.Null(parseErr);
        Assert.NotNull(ast);

        var result = QueryValidator.Validate(ast);
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);

        var err = result.Errors[0];
        Assert.Equal(expectedCode, err.Code);
        Assert.Equal(expectedPos, err.Position);
        Assert.Equal(expectedLen, err.Length);

        if (expectedSuggestion != null)
        {
            Assert.Equal(expectedSuggestion, err.Suggestion);
        }
    }

    [Fact]
    public void Validate_SizeUnitConversions_ConvertProperly()
    {
        var queries = new (string text, long expectedBytes)[]
        {
            ("size == 500", 500),
            ("size == 500b", 500),
            ("size == 1kb", 1024),
            ("size == 1.5kb", 1536),
            ("size == 2mb", 2 * 1024 * 1024),
            ("size == 1gb", 1024L * 1024 * 1024)
        };

        foreach (var (text, expectedBytes) in queries)
        {
            var (ast, _) = QueryParser.Parse(text);
            var result = QueryValidator.Validate(ast);
            Assert.True(result.IsValid);
            Assert.NotNull(result.BoundTree);

            var comp = Assert.IsType<BoundComparisonNode>(result.BoundTree);
            Assert.Equal(expectedBytes, (long)comp.Value);
        }
    }

    [Fact]
    public void Validate_JpegAlias_NormalizesToJpg()
    {
        var (ast, _) = QueryParser.Parse("type == jpeg");
        var result = QueryValidator.Validate(ast);
        Assert.True(result.IsValid);

        var comp = Assert.IsType<BoundComparisonNode>(result.BoundTree);
        Assert.Equal("jpg", (string)comp.Value);
    }
}
