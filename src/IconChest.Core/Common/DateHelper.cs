using System.Globalization;

namespace IconChest.Core.Common;

public static class DateHelper
{
    public static string DateToLocalNoonUtc(string dateString, TimeZoneInfo? timeZone = null)
    {
        var tz = timeZone ?? TimeZoneInfo.Local;

        if (!DateTime.TryParseExact(dateString, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            throw new ArgumentException($"Invalid date '{dateString}'. Expected format is yyyy-MM-dd.", nameof(dateString));
        }

        // Local noon (12:00:00) on the given date in the target time zone
        var localNoon = new DateTime(parsedDate.Year, parsedDate.Month, parsedDate.Day, 12, 0, 0, DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(localNoon, tz);
        return utc.ToString("yyyy-MM-ddTHH:mm:ssZ");
    }

    public static string? UtcToLocalDateString(string? utcString, TimeZoneInfo? timeZone = null)
    {
        if (string.IsNullOrWhiteSpace(utcString)) return null;

        var tz = timeZone ?? TimeZoneInfo.Local;
        if (!DateTime.TryParse(utcString, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsedUtc))
        {
            return null;
        }

        var local = TimeZoneInfo.ConvertTimeFromUtc(parsedUtc, tz);
        return local.ToString("yyyy-MM-dd");
    }

    public static string DetermineEffectiveDateSource(string? takenOverrideUtc, string? exifDateUtc, string? createdUtc, string? modifiedUtc)
    {
        if (!string.IsNullOrWhiteSpace(takenOverrideUtc)) return "override";
        if (!string.IsNullOrWhiteSpace(exifDateUtc)) return "exif";
        if (!string.IsNullOrWhiteSpace(createdUtc)) return "created";
        if (!string.IsNullOrWhiteSpace(modifiedUtc)) return "modified";
        return "none";
    }
}

