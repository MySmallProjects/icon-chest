using IconChest.Core.Query;
using Xunit;

namespace IconChest.Core.Tests.Query;

public class QueryParserTests
{
    [Fact]
    public void Parse_EmptyOrWhitespace_ReturnsNullAstAndNullError()
    {
        var (ast1, err1) = QueryParser.Parse("");
        Assert.Null(ast1);
        Assert.Null(err1);

        var (ast2, err2) = QueryParser.Parse("   \t\n  ");
        Assert.Null(ast2);
        Assert.Null(err2);
    }

    [Fact]
    public void Parse_Precedence_AndTighterThanOr()
    {
        // a or b and c should be parsed as: a or (b and c)
        var (ast, err) = QueryParser.Parse("type == png or tag == dog and rating >= 4");
        Assert.Null(err);
        Assert.NotNull(ast);

        var orNode = Assert.IsType<OrNode>(ast);
        Assert.IsType<ComparisonNode>(orNode.Left);

        var andNode = Assert.IsType<AndNode>(orNode.Right);
        Assert.IsType<ComparisonNode>(andNode.Left);
        Assert.IsType<ComparisonNode>(andNode.Right);
    }

    [Fact]
    public void Parse_Precedence_NotTighterThanAnd()
    {
        // not a and b should be: (not a) and b
        var (ast, err) = QueryParser.Parse("not type == png and tag == dog");
        Assert.Null(err);
        Assert.NotNull(ast);

        var andNode = Assert.IsType<AndNode>(ast);
        var notNode = Assert.IsType<NotNode>(andNode.Left);
        Assert.IsType<ComparisonNode>(notNode.Operand);
        Assert.IsType<ComparisonNode>(andNode.Right);
    }

    [Fact]
    public void Parse_Parentheses_OverridePrecedence()
    {
        var (ast, err) = QueryParser.Parse("(type == png or tag == dog) and rating >= 4");
        Assert.Null(err);
        Assert.NotNull(ast);

        var andNode = Assert.IsType<AndNode>(ast);
        var orNode = Assert.IsType<OrNode>(andNode.Left);
        Assert.IsType<ComparisonNode>(orNode.Left);
        Assert.IsType<ComparisonNode>(orNode.Right);
        Assert.IsType<ComparisonNode>(andNode.Right);
    }

    [Fact]
    public void Parse_ChainedNot_Allowed()
    {
        var (ast, err) = QueryParser.Parse("not not favourite");
        Assert.Null(err);
        Assert.NotNull(ast);

        var not1 = Assert.IsType<NotNode>(ast);
        var not2 = Assert.IsType<NotNode>(not1.Operand);
        var bare = Assert.IsType<BareTermNode>(not2.Operand);
        Assert.Equal("favourite", bare.Term.Text);
    }

    [Fact]
    public void Parse_AllGrammarRules_Succeed()
    {
        var queries = new[]
        {
            "tag in (dog, cat, mouse)",
            "tag not in (dog, cat)",
            "title is empty",
            "title is not empty",
            "filename like \"*.png\"",
            "filename not like \"*.gif\"",
            "description contains \"app\"",
            "description not contains \"beta\"",
            "folder under \"/home/user/icons\"",
            "folder not under \"/home/user/trash\"",
            "favourite",
            "!hidden",
            "\"elephant\"",
            "elephant"
        };

        foreach (var q in queries)
        {
            var (ast, err) = QueryParser.Parse(q);
            Assert.Null(err);
            Assert.NotNull(ast);
        }
    }

    [Fact]
    public void Parse_MissingConnective_ReturnsErrorWithSuggestion()
    {
        var (ast, err) = QueryParser.Parse("type == png tag == dog");
        Assert.Null(ast);
        Assert.NotNull(err);
        Assert.Equal(QueryErrorCodes.MissingConnective, err.Code);
        Assert.Equal("and", err.Suggestion);
        Assert.Equal(12, err.Position); // start of second term "tag"
    }

    [Theory]
    // 40+ syntax error cases
    [InlineData("(", QueryErrorCodes.UnexpectedEnd, 1, 0)]
    [InlineData("()", QueryErrorCodes.EmptyGroup, 0, 2)]
    [InlineData("tag in ()", QueryErrorCodes.EmptyList, 7, 2)]
    [InlineData("tag ==", QueryErrorCodes.ExpectedValue, 6, 0)]
    [InlineData("tag == and", QueryErrorCodes.ExpectedValue, 7, 3)]
    [InlineData("and tag == 1", QueryErrorCodes.UnexpectedToken, 0, 3)]
    [InlineData("or tag == 1", QueryErrorCodes.UnexpectedToken, 0, 2)]
    [InlineData("tag == 1 and", QueryErrorCodes.UnexpectedEnd, 12, 0)]
    [InlineData("tag == 1 or", QueryErrorCodes.UnexpectedEnd, 11, 0)]
    [InlineData("not", QueryErrorCodes.UnexpectedEnd, 3, 0)]
    [InlineData("not and", QueryErrorCodes.UnexpectedToken, 4, 3)]
    [InlineData(")", QueryErrorCodes.UnbalancedParenthesis, 0, 1)]
    [InlineData("(tag == 1", QueryErrorCodes.UnbalancedParenthesis, 0, 1)]
    [InlineData("tag == 1)", QueryErrorCodes.UnbalancedParenthesis, 8, 1)]
    [InlineData("tag in", QueryErrorCodes.ExpectedExpression, 6, 0)]
    [InlineData("tag in dog", QueryErrorCodes.ExpectedExpression, 7, 3)]
    [InlineData("tag in (dog", QueryErrorCodes.UnexpectedEnd, 11, 0)]
    [InlineData("tag in (dog,", QueryErrorCodes.UnexpectedEnd, 12, 0)]
    [InlineData("tag in (dog,,cat)", QueryErrorCodes.ExpectedValue, 12, 1)]
    [InlineData("tag in (dog cat)", QueryErrorCodes.UnexpectedToken, 12, 3)]
    [InlineData("tag not", QueryErrorCodes.MissingConnective, 4, 3)]
    [InlineData("tag not dog", QueryErrorCodes.MissingConnective, 4, 3)]
    [InlineData("tag not in", QueryErrorCodes.ExpectedExpression, 10, 0)]
    [InlineData("tag is", QueryErrorCodes.ExpectedOperator, 6, 0)]
    [InlineData("tag is dog", QueryErrorCodes.ExpectedOperator, 7, 3)]
    [InlineData("tag is not", QueryErrorCodes.ExpectedOperator, 10, 0)]
    [InlineData("tag is not dog", QueryErrorCodes.ExpectedOperator, 11, 3)]
    [InlineData("tag like", QueryErrorCodes.ExpectedValue, 8, 0)]
    [InlineData("tag not like", QueryErrorCodes.ExpectedValue, 12, 0)]
    [InlineData("tag contains", QueryErrorCodes.ExpectedValue, 12, 0)]
    [InlineData("tag not contains", QueryErrorCodes.ExpectedValue, 16, 0)]
    [InlineData("tag under", QueryErrorCodes.ExpectedValue, 9, 0)]
    [InlineData("tag not under", QueryErrorCodes.ExpectedValue, 13, 0)]
    [InlineData("tag == 1 tag == 2", QueryErrorCodes.MissingConnective, 9, 3)]
    [InlineData("dog cat mouse", QueryErrorCodes.MissingConnective, 4, 3)]
    [InlineData("tag == 1 (tag == 2)", QueryErrorCodes.MissingConnective, 9, 1)]
    [InlineData("(tag == 1) (tag == 2)", QueryErrorCodes.MissingConnective, 11, 1)]
    [InlineData("(tag == 1 and)", QueryErrorCodes.ExpectedExpression, 13, 1)]
    [InlineData("(tag == 1 or)", QueryErrorCodes.ExpectedExpression, 12, 1)]
    [InlineData("!(tag == 1 and)", QueryErrorCodes.ExpectedExpression, 14, 1)]
    [InlineData("tag in (1, 2,)", QueryErrorCodes.ExpectedValue, 13, 1)]
    [InlineData("((tag == 1)))", QueryErrorCodes.UnbalancedParenthesis, 12, 1)]
    public void Parse_SyntaxErrors_ReportsExactCodePositionAndLength(
        string query,
        string expectedCode,
        int expectedPos,
        int expectedLen)
    {
        var (ast, err) = QueryParser.Parse(query);
        Assert.Null(ast);
        Assert.NotNull(err);
        Assert.Equal(expectedCode, err.Code);
        Assert.Equal(expectedPos, err.Position);
        Assert.Equal(expectedLen, err.Length);
    }

    [Fact]
    public void Parse_NestingTooDeep_ReturnsNestingTooDeepError()
    {
        // 21 levels of parentheses
        var text = new string('(', 21) + "favourite" + new string(')', 21);
        var (ast, err) = QueryParser.Parse(text);

        Assert.Null(ast);
        Assert.NotNull(err);
        Assert.Equal(QueryErrorCodes.NestingTooDeep, err.Code);
    }
}
