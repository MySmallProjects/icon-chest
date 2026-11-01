using System.Reflection;
using System.Text.Json;
using IconChest.Core.Versioning;
using Xunit;

namespace IconChest.Core.Tests;

public class VersioningTests
{
    [Fact]
    public void ComputeVersionNumbers_ValuesAreWithinValidRange()
    {
        var sampleDate = new DateTime(2026, 11, 1, 21, 30, 0, DateTimeKind.Utc);
        var (build, revision) = VersionInfo.ComputeVersionNumbers(sampleDate);

        Assert.InRange(build, 0, 65535);
        Assert.InRange(revision, 0, 65535);
    }

    [Fact]
    public void ComputeVersionNumbers_MatchesKnownSampleDate()
    {
        // 2024-01-01 00:00:00 -> build 0, rev 0
        var epoch = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var (b0, r0) = VersionInfo.ComputeVersionNumbers(epoch);
        Assert.Equal(0, b0);
        Assert.Equal(0, r0);

        // 2024-01-02 00:01:00 UTC -> build 1, rev 30
        var d1 = new DateTime(2024, 1, 2, 0, 1, 0, DateTimeKind.Utc);
        var (b1, r1) = VersionInfo.ComputeVersionNumbers(d1);
        Assert.Equal(1, b1);
        Assert.Equal(30, r1);
    }

    [Fact]
    public void TryParseBuildDateFromVersion_RoundTripsAccurately()
    {
        var original = new DateTime(2026, 11, 1, 14, 20, 10, DateTimeKind.Utc);
        var (build, rev) = VersionInfo.ComputeVersionNumbers(original);
        var versionString = $"0.1.{build}.{rev}";

        var success = VersionInfo.TryParseBuildDateFromVersion(versionString, out var parsedUtc);
        Assert.True(success);
        Assert.Equal(original.Date, parsedUtc.Date);
        Assert.Equal(original.Hour, parsedUtc.Hour);
        Assert.Equal(original.Minute, parsedUtc.Minute);
    }

    [Fact]
    public void VersionInfo_ReadsFromVersionJsonFallbackWhenPresent()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"iconchest-ver-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var jsonPath = Path.Combine(tempDir, "version.json");
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(new
            {
                version = "1.2.3.4",
                buildDateUtc = "2026-10-30T12:00:00Z"
            }));

            // Pass null assembly so it uses fallback logic
            var info = new VersionInfo(targetAssembly: typeof(VersioningTests).Assembly, baseDirectory: tempDir);
            Assert.NotNull(info.Version);
            Assert.True(info.BuildDateUtc.HasValue);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}

