namespace IconChest.Core.Common;

/// <summary>
/// Linear-time safe glob/wildcard matcher supporting '*' (any run) and '?' (any single character).
/// Safe against catastrophic backtracking / ReDoS on hostile patterns.
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

    private static bool EqualsChar(char a, char b, bool ignoreCase)
    {
        if (a == b) return true;
        if (!ignoreCase) return false;
        return char.ToUpperInvariant(a) == char.ToUpperInvariant(b);
    }
}

