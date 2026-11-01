using IconChest.Core.Common;
using IconChest.Core.Logging;
using Microsoft.Extensions.Logging;
using Xunit;

namespace IconChest.Core.Tests;

public class LoggingTests
{
    private sealed class FakeClock : IClock
    {
        public DateTime UtcNow { get; set; }
        public FakeClock(DateTime initialUtc) => UtcNow = initialUtc;
    }

    [Fact]
    public void FileName_ContainsComponentAndDate()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"log-test-{Guid.NewGuid():N}");
        var fakeClock = new FakeClock(new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc));

        try
        {
            using var manager = new AppLogging(LogComponent.App, tempDir, AppLogLevel.Information, fakeClock);
            Assert.EndsWith("app-20261101.log", manager.CurrentLogFilePath);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void LevelFiltering_PassesAndBlocksCorrectMessages()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"log-test-{Guid.NewGuid():N}");
        var fakeClock = new FakeClock(new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc));

        try
        {
            using (var manager = new AppLogging(LogComponent.App, tempDir, AppLogLevel.Warning, fakeClock))
            {
                var logger = manager.CreateLogger("TestCategory");
                logger.LogDebug("debug-msg-should-be-filtered");
                logger.LogInformation("info-msg-should-be-filtered");
                logger.LogWarning("warning-msg-should-be-written");
                logger.LogError("error-msg-should-be-written");
            }

            var logFile = Path.Combine(tempDir, "app-20261101.log");
            Assert.True(File.Exists(logFile));
            var content = File.ReadAllText(logFile);

            Assert.DoesNotContain("debug-msg-should-be-filtered", content);
            Assert.DoesNotContain("info-msg-should-be-filtered", content);
            Assert.Contains("warning-msg-should-be-written", content);
            Assert.Contains("error-msg-should-be-written", content);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void OffLevel_WritesNothing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"log-test-{Guid.NewGuid():N}");
        var fakeClock = new FakeClock(new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc));

        try
        {
            using (var manager = new AppLogging(LogComponent.App, tempDir, AppLogLevel.Off, fakeClock))
            {
                var logger = manager.CreateLogger("TestCategory");
                logger.LogError("critical-failure-message");
                logger.LogWarning("warning-message");
            }

            var logFile = Path.Combine(tempDir, "app-20261101.log");
            if (File.Exists(logFile))
            {
                var content = File.ReadAllText(logFile);
                Assert.Empty(content);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void RuntimeLevelChange_TakesEffectImmediately()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"log-test-{Guid.NewGuid():N}");
        var fakeClock = new FakeClock(new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc));

        try
        {
            using (var manager = new AppLogging(LogComponent.App, tempDir, AppLogLevel.Error, fakeClock))
            {
                var logger = manager.CreateLogger("TestCategory");
                logger.LogInformation("info-before-level-change");

                // Switch level at runtime without restart
                manager.SetLevel(AppLogLevel.Information);
                logger.LogInformation("info-after-level-change");
            }

            var logFile = Path.Combine(tempDir, "app-20261101.log");
            Assert.True(File.Exists(logFile));
            var content = File.ReadAllText(logFile);

            Assert.DoesNotContain("info-before-level-change", content);
            Assert.Contains("info-after-level-change", content);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Retention_DeletesFilesOlderThan30Days_PreservesRecentAndOtherComponents()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"log-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var fakeClock = new FakeClock(new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc));

        try
        {
            // 45 days old - should be deleted
            var oldAppLog = Path.Combine(tempDir, "app-20260915.log");
            File.WriteAllText(oldAppLog, "old app log");

            // 10 days old - should be kept
            var recentAppLog = Path.Combine(tempDir, "app-20261022.log");
            File.WriteAllText(recentAppLog, "recent app log");

            // 45 days old API log - should NOT be deleted by app retention cleanup
            var oldApiLog = Path.Combine(tempDir, "api-20260915.log");
            File.WriteAllText(oldApiLog, "old api log");

            using (var manager = new AppLogging(LogComponent.App, tempDir, AppLogLevel.Information, fakeClock))
            {
                manager.RunRetentionCleanup();
            }

            Assert.False(File.Exists(oldAppLog), "Expired app log should have been deleted");
            Assert.True(File.Exists(recentAppLog), "Recent app log should be kept");
            Assert.True(File.Exists(oldApiLog), "Different component log should not be deleted");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Retention_LockedFile_DoesNotThrow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"log-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var fakeClock = new FakeClock(new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc));

        try
        {
            var oldAppLog = Path.Combine(tempDir, "app-20260915.log");
            File.WriteAllText(oldAppLog, "locked old log");

            // Lock file exclusively
            using var stream = new FileStream(oldAppLog, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            using var manager = new AppLogging(LogComponent.App, tempDir, AppLogLevel.Information, fakeClock);
            // Must not throw exception
            var exception = Record.Exception(() => manager.RunRetentionCleanup());
            Assert.Null(exception);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }
}

