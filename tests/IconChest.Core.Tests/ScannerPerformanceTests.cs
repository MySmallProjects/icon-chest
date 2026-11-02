using System.Diagnostics;
using IconChest.Core.Common;
using IconChest.Core.Data;
using IconChest.Core.Data.Repositories;
using IconChest.Core.Scanning;
using IconChest.Core.Settings;
using IconChest.Core.Tests.TestHelpers;
using Xunit;
using Xunit.Abstractions;

namespace IconChest.Core.Tests;

public class ScannerPerformanceTests
{
    private readonly ITestOutputHelper _output;

    public ScannerPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Category", "Explicit")]
    public async Task ScanPerformance_5000Files_InitialAndRescan()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"scanner-perf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var configDir = Path.Combine(tempDir, "config");
        var dataDir = Path.Combine(tempDir, "data");
        var paths = new AppPaths(configDirOverride: configDir, dataDirOverride: dataDir, isWindowsOverride: false);
        var settings = new SettingsService(paths);

        var db = new IconChestDatabase(settings);
        db.Migrate();

        var connFactory = new DatabaseConnectionFactory(db.DatabasePath);
        var folderRepo = new LibraryFolderRepository(connFactory);
        var imageRepo = new ImageRepository(connFactory);

        var scanner = new LibraryScanner(
            folderRepo,
            imageRepo,
            settings,
            new XxHash128Hasher(),
            new ImageMetadataReader());

        try
        {
            var libraryDir = Path.Combine(tempDir, "LargeLibrary");
            Directory.CreateDirectory(libraryDir);

            // Pre-generate sample bytes for different formats to speed up fixture creation
            var pngBytes = ImageTestFixtures.CreatePng(16, 16, transparent: false);
            var svgContent = ImageTestFixtures.CreateSvg(32, 32);

            _output.WriteLine("Generating 5,000 files across 10 subdirectories...");
            var genSw = Stopwatch.StartNew();

            const int totalFiles = 5000;
            const int subdirs = 10;
            int filesPerDir = totalFiles / subdirs;

            for (int d = 0; d < subdirs; d++)
            {
                var subDirPath = Path.Combine(libraryDir, $"dir_{d:D2}");
                Directory.CreateDirectory(subDirPath);

                for (int i = 0; i < filesPerDir; i++)
                {
                    var fileIndex = (d * filesPerDir) + i;
                    // Mix PNG and SVG files
                    if (fileIndex % 2 == 0)
                    {
                        var filePath = Path.Combine(subDirPath, $"icon_{fileIndex:D5}.png");
                        File.WriteAllBytes(filePath, pngBytes);
                    }
                    else
                    {
                        var filePath = Path.Combine(subDirPath, $"icon_{fileIndex:D5}.svg");
                        File.WriteAllText(filePath, svgContent);
                    }
                }
            }
            genSw.Stop();
            _output.WriteLine($"Generated {totalFiles} files in {genSw.ElapsedMilliseconds} ms.");

            var folderId = folderRepo.Add(libraryDir);

            // 1. Initial Scan
            _output.WriteLine("Starting initial scan...");
            var initialSw = Stopwatch.StartNew();
            var initialResult = await scanner.ScanAsync(folderId);
            initialSw.Stop();

            _output.WriteLine($"Initial scan completed in {initialSw.ElapsedMilliseconds} ms. Found: {initialResult.FilesFound}, New: {initialResult.NewFiles}, Errors: {initialResult.Errors}");

            Assert.Equal(totalFiles, initialResult.FilesFound);
            Assert.Equal(totalFiles, initialResult.NewFiles);
            Assert.Equal(0, initialResult.Errors);

            var stats = imageRepo.GetStats();
            Assert.Equal(totalFiles, stats.ImagesCount);

            // 2. Incremental Rescan (Nothing changed)
            _output.WriteLine("Starting incremental rescan (no changes)...");
            var rescanSw = Stopwatch.StartNew();
            var rescanResult = await scanner.ScanAsync(folderId);
            rescanSw.Stop();

            _output.WriteLine($"Incremental rescan completed in {rescanSw.ElapsedMilliseconds} ms. Found: {rescanResult.FilesFound}, New: {rescanResult.NewFiles}, Unchanged: {rescanResult.UnchangedFiles}, Errors: {rescanResult.Errors}");

            Assert.Equal(totalFiles, rescanResult.FilesFound);
            Assert.Equal(0, rescanResult.NewFiles);
            Assert.Equal(totalFiles, rescanResult.UnchangedFiles);
            Assert.Equal(0, rescanResult.Errors);

            // Rescan should be significantly faster than initial scan because no hashing or metadata reading occurs
            Assert.True(rescanSw.ElapsedMilliseconds < initialSw.ElapsedMilliseconds,
                $"Rescan ({rescanSw.ElapsedMilliseconds} ms) should be faster than initial scan ({initialSw.ElapsedMilliseconds} ms)");
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }
}
