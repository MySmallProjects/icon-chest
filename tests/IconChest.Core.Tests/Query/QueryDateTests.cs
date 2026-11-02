using IconChest.Core.Common;
using IconChest.Core.Query;
using Xunit;

namespace IconChest.Core.Tests.Query;

public class QueryDateTests
{
    private static TimeZoneInfo GetLondonTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
        }
        catch
        {
            // Windows ID fallback
            return TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
        }
    }

    [Fact]
    public void Date_DstSpringForward_BoundsSpan23Hours()
    {
        // On 2026-03-29, London springs forward from 01:00 to 02:00 (UTC+0 -> UTC+1)
        var londonTz = GetLondonTimeZone();
        bool success = QueryValidator.TryParseDateBounds("2026-03-29", londonTz, out var startUtc, out var endUtc);

        Assert.True(success);
        Assert.Equal(new DateTime(2026, 3, 29, 0, 0, 0, DateTimeKind.Utc), startUtc);
        // Midnight on 2026-03-30 in London is 23:00 UTC on 2026-03-29 (23 hours total!)
        Assert.Equal(new DateTime(2026, 3, 29, 23, 0, 0, DateTimeKind.Utc), endUtc);
        Assert.Equal(TimeSpan.FromHours(23), endUtc - startUtc);
    }

    [Fact]
    public void Date_DstFallBack_BoundsSpan25Hours()
    {
        // On 2026-10-25, London falls back from 02:00 to 01:00 (UTC+1 -> UTC+0)
        var londonTz = GetLondonTimeZone();
        bool success = QueryValidator.TryParseDateBounds("2026-10-25", londonTz, out var startUtc, out var endUtc);

        Assert.True(success);
        // Midnight on 2026-10-25 in London is 23:00 UTC on 2026-10-24
        Assert.Equal(new DateTime(2026, 10, 24, 23, 0, 0, DateTimeKind.Utc), startUtc);
        // Midnight on 2026-10-26 in London is 00:00 UTC on 2026-10-26 (25 hours total!)
        Assert.Equal(new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc), endUtc);
        Assert.Equal(TimeSpan.FromHours(25), endUtc - startUtc);
    }

    [Fact]
    public void Date_YearAndMonthBounds_AreAccurate()
    {
        var utc = TimeZoneInfo.Utc;

        // Whole year 2026
        Assert.True(QueryValidator.TryParseDateBounds("2026", utc, out var startYear, out var endYear));
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), startYear);
        Assert.Equal(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), endYear);

        // Whole month 2026-02
        Assert.True(QueryValidator.TryParseDateBounds("2026-02", utc, out var startFeb, out var endFeb));
        Assert.Equal(new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), startFeb);
        Assert.Equal(new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), endFeb);

        // Whole month 2026-12 (cross-year boundary)
        Assert.True(QueryValidator.TryParseDateBounds("2026-12", utc, out var startDec, out var endDec));
        Assert.Equal(new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc), startDec);
        Assert.Equal(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), endDec);
    }

    [Theory]
    [InlineData("date == 2026-05-10", "image.effective_date_utc >= @q0 AND image.effective_date_utc < @q1")]
    [InlineData("date != 2026-05-10", "image.effective_date_utc < @q0 OR image.effective_date_utc >= @q1")]
    [InlineData("date < 2026-05-10", "image.effective_date_utc < @q0")]
    [InlineData("date <= 2026-05-10", "image.effective_date_utc < @q1")]
    [InlineData("date > 2026-05-10", "image.effective_date_utc >= @q1")]
    [InlineData("date >= 2026-05-10", "image.effective_date_utc >= @q0")]
    public void Date_ComparisonOperators_GenerateCorrectBoundaryChecks(string query, string expectedFragment)
    {
        var comp = QueryEngine.ParseAndCompile(query, TimeZoneInfo.Utc);
        Assert.True(comp.IsValid);
        Assert.NotNull(comp.Query);

        Assert.Contains(expectedFragment, comp.Query.WhereSql);
    }
}

