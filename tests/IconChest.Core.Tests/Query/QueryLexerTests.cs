using IconChest.Core.Query;
using Xunit;

namespace IconChest.Core.Tests.Query;

public class QueryLexerTests
{
    [Fact]
    public void Tokenize_SymbolsAndOperators_ReturnsCorrectTokensAndPositions()
    {
        var text = "( ) , == = != < <= > >= && || !";
        var (tokens, error) = QueryLexer.Tokenize(text);

        Assert.Null(error);
        Assert.NotNull(tokens);

        var types = tokens.Select(t => t.Type).ToArray();
        var expected = new[]
        {
            QueryTokenType.LParen,
            QueryTokenType.RParen,
            QueryTokenType.Comma,
            QueryTokenType.CmpOp, // ==
            QueryTokenType.CmpOp, // =
            QueryTokenType.CmpOp, // !=
            QueryTokenType.CmpOp, // <
            QueryTokenType.CmpOp, // <=
            QueryTokenType.CmpOp, // >
            QueryTokenType.CmpOp, // >=
            QueryTokenType.And,   // &&
            QueryTokenType.Or,    // ||
            QueryTokenType.Not,   // !
            QueryTokenType.EndOfInput
        };

        Assert.Equal(expected, types);
    }

    [Fact]
    public void Tokenize_Keywords_AreCaseInsensitive()
    {
        var text = "AND and Or OR not NOT in IN is IS empty EMPTY like LIKE contains CONTAINS under UNDER true TRUE false FALSE";
        var (tokens, error) = QueryLexer.Tokenize(text);

        Assert.Null(error);
        Assert.NotNull(tokens);

        Assert.Equal(23, tokens.Count); // 22 keywords + EndOfInput
        Assert.Equal(QueryTokenType.And, tokens[0].Type);
        Assert.Equal(QueryTokenType.And, tokens[1].Type);
        Assert.Equal(QueryTokenType.Or, tokens[2].Type);
        Assert.Equal(QueryTokenType.Or, tokens[3].Type);
        Assert.Equal(QueryTokenType.Not, tokens[4].Type);
        Assert.Equal(QueryTokenType.Not, tokens[5].Type);
        Assert.Equal(QueryTokenType.In, tokens[6].Type);
        Assert.Equal(QueryTokenType.In, tokens[7].Type);
        Assert.Equal(QueryTokenType.Is, tokens[8].Type);
        Assert.Equal(QueryTokenType.Is, tokens[9].Type);
        Assert.Equal(QueryTokenType.Empty, tokens[10].Type);
        Assert.Equal(QueryTokenType.Empty, tokens[11].Type);
        Assert.Equal(QueryTokenType.Like, tokens[12].Type);
        Assert.Equal(QueryTokenType.Like, tokens[13].Type);
        Assert.Equal(QueryTokenType.Contains, tokens[14].Type);
        Assert.Equal(QueryTokenType.Contains, tokens[15].Type);
        Assert.Equal(QueryTokenType.Under, tokens[16].Type);
        Assert.Equal(QueryTokenType.Under, tokens[17].Type);
        Assert.Equal(QueryTokenType.Word, tokens[18].Type); // true/false are words with boolean meaning
        Assert.Equal("true", tokens[18].Text, ignoreCase: true);
    }

    [Fact]
    public void Tokenize_UnicodeWordsAndAccents_ParsedProperly()
    {
        var text = "élégant naïve 日本語_icon 123-abc";
        var (tokens, error) = QueryLexer.Tokenize(text);

        Assert.Null(error);
        Assert.NotNull(tokens);
        Assert.Equal(5, tokens.Count); // 4 words + EndOfInput

        Assert.Equal("élégant", tokens[0].Text);
        Assert.Equal("naïve", tokens[1].Text);
        Assert.Equal("日本語_icon", tokens[2].Text);
        Assert.Equal("123-abc", tokens[3].Text);
    }

    [Theory]
    [InlineData(@"""hello world""", "hello world")]
    [InlineData(@"'single quoted'", "single quoted")]
    [InlineData(@"""with \"" quote""", @"with "" quote")]
    [InlineData(@"""with \\ backslash""", @"with \ backslash")]
    [InlineData(@"""with \* star and \? question""", @"with \* star and \? question")]
    public void Tokenize_QuotedStrings_ResolvesEscapes(string input, string expected)
    {
        var (tokens, error) = QueryLexer.Tokenize(input);
        Assert.Null(error);
        Assert.NotNull(tokens);
        Assert.Equal(QueryTokenType.String, tokens[0].Type);
        Assert.Equal(expected, tokens[0].Text);
    }

    [Fact]
    public void Tokenize_UnterminatedString_ReturnsUnterminatedStringErrorWithCaret()
    {
        var text = "tag == \"unterminated";
        var (tokens, error) = QueryLexer.Tokenize(text);

        Assert.Null(tokens);
        Assert.NotNull(error);
        Assert.Equal(QueryErrorCodes.UnterminatedString, error.Code);
        Assert.Equal(7, error.Position);
        Assert.Equal(13, error.Length); // from opening quote to end of input
    }

    [Fact]
    public void Tokenize_LoneAmpersandOrPipe_ReturnsUnexpectedCharacterError()
    {
        var (t1, e1) = QueryLexer.Tokenize("tag == 1 & tag == 2");
        Assert.Null(t1);
        Assert.NotNull(e1);
        Assert.Equal(QueryErrorCodes.UnexpectedCharacter, e1.Code);
        Assert.Equal(9, e1.Position);
        Assert.Equal(1, e1.Length);

        var (t2, e2) = QueryLexer.Tokenize("tag == 1 | tag == 2");
        Assert.Null(t2);
        Assert.NotNull(e2);
        Assert.Equal(QueryErrorCodes.UnexpectedCharacter, e2.Code);
        Assert.Equal(9, e2.Position);
    }

    [Fact]
    public void Tokenize_QueryTooLong_ReturnsQueryTooLongError()
    {
        var text = new string('a', 2005);
        var (tokens, error) = QueryLexer.Tokenize(text);

        Assert.Null(tokens);
        Assert.NotNull(error);
        Assert.Equal(QueryErrorCodes.QueryTooLong, error.Code);
        Assert.Equal(2000, error.Position);
        Assert.Equal(5, error.Length);
    }

    [Fact]
    public void Tokenize_MultiByteUtf16Positions_AreAccurate()
    {
        // 🚀 is a surrogate pair (2 UTF-16 code units)
        var text = "tag == \"🚀\" and rating >= 4";
        var (tokens, error) = QueryLexer.Tokenize(text);

        Assert.Null(error);
        Assert.NotNull(tokens);

        // "tag" at 0..3
        Assert.Equal(0, tokens[0].Position);
        // "==" at 4..2
        Assert.Equal(4, tokens[1].Position);
        // "\"🚀\"" at 7..4
        Assert.Equal(7, tokens[2].Position);
        Assert.Equal(4, tokens[2].Length);
        // "and" at 12..3
        Assert.Equal(12, tokens[3].Position);
    }
}
