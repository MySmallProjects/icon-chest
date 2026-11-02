using System.Text;

namespace IconChest.Core.Common;

/// <summary>
/// Linear-time safe glob/wildcard matcher supporting '*' (any run) and '?' (any single character).
/// Safe against catastrophic backtracking / ReDoS on hostile patterns.
/// Also supports pattern escaping where '\*', '\?', and '\\' denote literal characters.
/// </summary>
public static class WildcardMatcher
{
    public static bool IsMatch(string text, string pattern, bool ignoreCase = true)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(pattern);

        return IsMatch(text.AsSpan(), pattern.AsSpan(), ignoreCase);
    }

    public static bool IsMatch(ReadOnlySpan<char> text, ReadOnlySpan<char> pattern, bool ignoreCase = true)
    {
        var textIdx = 0;
        var patIdx = 0;
        var starPatIdx = -1;
        var textBacktrackIdx = -1;

        while (textIdx < text.Length)
        {
            if (patIdx < pattern.Length && (pattern[patIdx] == '?' || EqualsChar(pattern[patIdx], text[textIdx], ignoreCase)))
            {
                textIdx++;
                patIdx++;
            }
            else if (patIdx < pattern.Length && pattern[patIdx] == '*')
            {
                starPatIdx = patIdx;
                textBacktrackIdx = textIdx;
                patIdx++;
            }
            else if (starPatIdx != -1)
            {
                // Backtrack to the most recent '*'
                patIdx = starPatIdx + 1;
                textBacktrackIdx++;
                textIdx = textBacktrackIdx;
            }
            else
            {
                return false;
            }
        }

        while (patIdx < pattern.Length && pattern[patIdx] == '*')
        {
            patIdx++;
        }

        return patIdx == pattern.Length;
    }

    public enum TokenType : byte
    {
        Literal,
        Single,
        AnyRun
    }

    public readonly struct Token
    {
        public TokenType Type { get; }
        public char Char { get; }

        public Token(TokenType type, char c = '\0')
        {
            Type = type;
            Char = c;
        }
    }

    /// <summary>
    /// Parses a pattern containing '\*', '\?', '\\' escapes into normalized tokens.
    /// Both text and pattern are NFC-normalized and lowercased for Unicode-aware comparison.
    /// </summary>
    public static bool IsMatchWithEscapes(string? text, string? pattern)
    {
        if (text is null || pattern is null) return false;

        var normText = text.Normalize(NormalizationForm.FormC).ToLowerInvariant();
        var normPat = pattern.Normalize(NormalizationForm.FormC).ToLowerInvariant();

        var tokens = TokenizePattern(normPat);
        return IsMatchTokens(normText.AsSpan(), tokens);
    }

    public static List<Token> TokenizePattern(string pattern)
    {
        var tokens = new List<Token>(pattern.Length);
        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            if (c == '\\' && i + 1 < pattern.Length)
            {
                char next = pattern[i + 1];
                if (next is '*' or '?' or '\\')
                {
                    tokens.Add(new Token(TokenType.Literal, next));
                    i++;
                    continue;
                }
                // Backslash before any other character is a literal backslash
                tokens.Add(new Token(TokenType.Literal, '\\'));
            }
            else if (c == '*')
            {
                // Collapse consecutive stars into a single AnyRun
                if (tokens.Count == 0 || tokens[^1].Type != TokenType.AnyRun)
                {
                    tokens.Add(new Token(TokenType.AnyRun));
                }
            }
            else if (c == '?')
            {
                tokens.Add(new Token(TokenType.Single));
            }
            else
            {
                tokens.Add(new Token(TokenType.Literal, c));
            }
        }

        return tokens;
    }

    public static bool IsMatchTokens(ReadOnlySpan<char> text, IReadOnlyList<Token> tokens)
    {
        int textIdx = 0;
        int tokIdx = 0;
        int starTokIdx = -1;
        int textBacktrackIdx = -1;

        while (textIdx < text.Length)
        {
            if (tokIdx < tokens.Count &&
                (tokens[tokIdx].Type == TokenType.Single ||
                 (tokens[tokIdx].Type == TokenType.Literal && tokens[tokIdx].Char == text[textIdx])))
            {
                textIdx++;
                tokIdx++;
            }
            else if (tokIdx < tokens.Count && tokens[tokIdx].Type == TokenType.AnyRun)
            {
                starTokIdx = tokIdx;
                textBacktrackIdx = textIdx;
                tokIdx++;
            }
            else if (starTokIdx != -1)
            {
                tokIdx = starTokIdx + 1;
                textBacktrackIdx++;
                textIdx = textBacktrackIdx;
            }
            else
            {
                return false;
            }
        }

        while (tokIdx < tokens.Count && tokens[tokIdx].Type == TokenType.AnyRun)
        {
            tokIdx++;
        }

        return tokIdx == tokens.Count;
    }

    private static bool EqualsChar(char a, char b, bool ignoreCase)
    {
        if (a == b) return true;
        if (!ignoreCase) return false;
        return char.ToUpperInvariant(a) == char.ToUpperInvariant(b);
    }
}
