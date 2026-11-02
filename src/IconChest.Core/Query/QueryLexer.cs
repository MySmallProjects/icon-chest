using System.Text;

namespace IconChest.Core.Query;

public sealed class QueryLexer
{
    public const int MaxQueryLength = 2000;

    public static (List<QueryToken>? Tokens, QueryError? Error) Tokenize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length > MaxQueryLength)
        {
            return (null, new QueryError(
                QueryErrorCodes.QueryTooLong,
                $"Query length exceeds maximum allowed limit of {MaxQueryLength} characters (length: {text.Length}).",
                MaxQueryLength,
                text.Length - MaxQueryLength,
                "Shorten the query."));
        }

        var tokens = new List<QueryToken>();
        int i = 0;
        int len = text.Length;

        while (i < len)
        {
            char c = text[i];

            // 1. Whitespace
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            // 2. Parentheses & comma
            if (c == '(')
            {
                tokens.Add(new QueryToken(QueryTokenType.LParen, "(", i, 1));
                i++;
                continue;
            }
            if (c == ')')
            {
                tokens.Add(new QueryToken(QueryTokenType.RParen, ")", i, 1));
                i++;
                continue;
            }
            if (c == ',')
            {
                tokens.Add(new QueryToken(QueryTokenType.Comma, ",", i, 1));
                i++;
                continue;
            }

            // 3. Comparison operators & connectives
            if (c == '=')
            {
                if (i + 1 < len && text[i + 1] == '=')
                {
                    tokens.Add(new QueryToken(QueryTokenType.CmpOp, "==", i, 2));
                    i += 2;
                }
                else
                {
                    tokens.Add(new QueryToken(QueryTokenType.CmpOp, "=", i, 1));
                    i++;
                }
                continue;
            }

            if (c == '!')
            {
                if (i + 1 < len && text[i + 1] == '=')
                {
                    tokens.Add(new QueryToken(QueryTokenType.CmpOp, "!=", i, 2));
                    i += 2;
                }
                else
                {
                    tokens.Add(new QueryToken(QueryTokenType.Not, "!", i, 1));
                    i++;
                }
                continue;
            }

            if (c == '<')
            {
                if (i + 1 < len && text[i + 1] == '=')
                {
                    tokens.Add(new QueryToken(QueryTokenType.CmpOp, "<=", i, 2));
                    i += 2;
                }
                else
                {
                    tokens.Add(new QueryToken(QueryTokenType.CmpOp, "<", i, 1));
                    i++;
                }
                continue;
            }

            if (c == '>')
            {
                if (i + 1 < len && text[i + 1] == '=')
                {
                    tokens.Add(new QueryToken(QueryTokenType.CmpOp, ">=", i, 2));
                    i += 2;
                }
                else
                {
                    tokens.Add(new QueryToken(QueryTokenType.CmpOp, ">", i, 1));
                    i++;
                }
                continue;
            }

            if (c == '&')
            {
                if (i + 1 < len && text[i + 1] == '&')
                {
                    tokens.Add(new QueryToken(QueryTokenType.And, "&&", i, 2));
                    i += 2;
                    continue;
                }

                return (null, new QueryError(
                    QueryErrorCodes.UnexpectedCharacter,
                    "Unexpected character '&'. A lone '&' is not permitted.",
                    i,
                    1,
                    "Did you mean '&&' or 'and'?"));
            }

            if (c == '|')
            {
                if (i + 1 < len && text[i + 1] == '|')
                {
                    tokens.Add(new QueryToken(QueryTokenType.Or, "||", i, 2));
                    i += 2;
                    continue;
                }

                return (null, new QueryError(
                    QueryErrorCodes.UnexpectedCharacter,
                    "Unexpected character '|'. A lone '|' is not permitted.",
                    i,
                    1,
                    "Did you mean '||' or 'or'?"));
            }

            // 4. Strings: "..." or '...'
            if (c is '"' or '\'')
            {
                char quote = c;
                int start = i;
                i++; // skip quote
                var sb = new StringBuilder();
                var rawSb = new StringBuilder();
                bool terminated = false;

                while (i < len)
                {
                    char ch = text[i];
                    if (ch == '\\')
                    {
                        if (i + 1 < len)
                        {
                            char next = text[i + 1];
                            rawSb.Append(ch);
                            rawSb.Append(next);

                            if (next == quote)
                            {
                                sb.Append(quote);
                                i += 2;
                                continue;
                            }
                            if (next == '\\')
                            {
                                sb.Append('\\');
                                i += 2;
                                continue;
                            }
                            if (next is '*' or '?')
                            {
                                sb.Append('\\');
                                sb.Append(next);
                                i += 2;
                                continue;
                            }

                            // Backslash before any other character
                            sb.Append('\\');
                            sb.Append(next);
                            i += 2;
                            continue;
                        }

                        // Backslash right at the end of input
                        rawSb.Append(ch);
                        sb.Append(ch);
                        i++;
                        break;
                    }

                    if (ch == quote)
                    {
                        terminated = true;
                        i++; // skip closing quote
                        break;
                    }

                    rawSb.Append(ch);
                    sb.Append(ch);
                    i++;
                }

                if (!terminated)
                {
                    int spanLen = len - start;
                    string? hint = null;
                    if (text.EndsWith($"\\{quote}"))
                    {
                        hint = $"A backslash before the quote escapes it. If you intended a path ending with a backslash, remove the trailing backslash.";
                    }

                    return (null, new QueryError(
                        QueryErrorCodes.UnterminatedString,
                        "Unterminated string literal.",
                        start,
                        spanLen,
                        hint ?? $"Add a closing {quote} quote."));
                }

                tokens.Add(new QueryToken(QueryTokenType.String, sb.ToString(), start, i - start, rawSb.ToString()));
                continue;
            }

            // 5. Words (unquoted run of letters, digits, and _ - . * ?)
            if (IsWordChar(c))
            {
                int start = i;
                while (i < len && IsWordChar(text[i]))
                {
                    i++;
                }

                string word = text[start..i];
                int tokenLen = i - start;

                // Check keywords (case-insensitive)
                var tokenType = ResolveKeyword(word);
                tokens.Add(new QueryToken(tokenType, word, start, tokenLen));
                continue;
            }

            // 6. Unknown / unexpected character
            return (null, new QueryError(
                QueryErrorCodes.UnexpectedCharacter,
                $"Unexpected character '{c}'.",
                i,
                1));
        }

        tokens.Add(new QueryToken(QueryTokenType.EndOfInput, string.Empty, len, 0));
        return (tokens, null);
    }

    public static bool IsWordChar(char c)
    {
        return char.IsLetter(c) ||
               char.IsDigit(c) ||
               c is '_' or '-' or '.' or '*' or '?';
    }

    private static QueryTokenType ResolveKeyword(string word)
    {
        if (word.Equals("and", StringComparison.OrdinalIgnoreCase)) return QueryTokenType.And;
        if (word.Equals("or", StringComparison.OrdinalIgnoreCase)) return QueryTokenType.Or;
        if (word.Equals("not", StringComparison.OrdinalIgnoreCase)) return QueryTokenType.Not;
        if (word.Equals("in", StringComparison.OrdinalIgnoreCase)) return QueryTokenType.In;
        if (word.Equals("is", StringComparison.OrdinalIgnoreCase)) return QueryTokenType.Is;
        if (word.Equals("empty", StringComparison.OrdinalIgnoreCase)) return QueryTokenType.Empty;
        if (word.Equals("like", StringComparison.OrdinalIgnoreCase)) return QueryTokenType.Like;
        if (word.Equals("contains", StringComparison.OrdinalIgnoreCase)) return QueryTokenType.Contains;
        if (word.Equals("under", StringComparison.OrdinalIgnoreCase)) return QueryTokenType.Under;

        return QueryTokenType.Word;
    }
}

