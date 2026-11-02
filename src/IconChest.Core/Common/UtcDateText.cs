using System.Globalization;

namespace IconChest.Core.Common;

/// <summary>
/// The single shared formatter for UTC timestamps stored as text in SQLite and for the
/// bounds used to compare against them.
/// </summary>
/// <remarks>
/// Stored values use the round-trip form <c>yyyy-MM-ddTHH:mm:ss.fffffffZ</c> (identical to
/// <c>DateTime.ToString("o")</c> for UTC values). Older rows may also contain
/// <c>yyyy-MM-ddTHH:mm:ssZ</c> (EXIF) or a <c>+00:00</c> suffix. Comparison bounds are therefore
/// written as the 19-character prefix <c>yyyy-MM-ddTHH:mm:ss</c>: any stored value at or after the
/// bound instant sorts at or after the prefix, whatever suffix it carries, and any value before
/// the bound instant sorts before it.
/// </remarks>
public static class UtcDateText
{
    public const string StorageFormat = "yyyy-MM-ddTHH:mm:ss.fffffffZ";
    public const string BoundFormat = "yyyy-MM-ddTHH:mm:ss";

    /// <summary>Formats a timestamp for storage. Unspecified kinds are treated as UTC.</summary>
    public static string Format(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Local => value.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _ => value
        };
        return utc.ToString(StorageFormat, CultureInfo.InvariantCulture);
    }

    public static string? Format(DateTime? value) => value.HasValue ? Format(value.Value) : null;

    /// <summary>Formats a UTC instant as a comparison bound (see remarks on the class).</summary>
    public static string FormatBound(DateTime utc)
    {
        var u = utc.Kind == DateTimeKind.Local ? utc.ToUniversalTime() : utc;
        return u.ToString(BoundFormat, CultureInfo.InvariantCulture);
    }
}

