namespace IconChest.Desktop;

public static class ReadinessLineParser
{
    public const string ReadinessPrefix = "ICONCHEST_READY ";

    public static Uri? TryParse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var trimmed = line.Trim();
        if (!trimmed.StartsWith(ReadinessPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var urlPart = trimmed.Substring(ReadinessPrefix.Length).Trim();
        if (string.IsNullOrEmpty(urlPart))
        {
            return null;
        }

        if (Uri.TryCreate(urlPart, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return uri;
        }

        return null;
    }
}

