using System.Diagnostics;
using IconChest.Core.Common;
using IconChest.Core.Data;
using IconChest.Core.Data.Models;
using IconChest.Core.Data.Repositories;
using IconChest.Core.Scanning;
using IconChest.Core.Settings;
using IconChest.Core.Tests.TestHelpers;
using IconChest.Core.Thumbnails;
using Xunit;
using Xunit.Abstractions;

namespace IconChest.Core.Tests;

public class ThumbnailPerformanceTests
{
    private readonly ITestOutputHelper _output;

    public ThumbnailPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Category", "Explicit")]
    public async Task ThumbnailPerformance_2000Images_1And4Workers()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"thumb-perf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var configDir = Path.Combine(tempDir, "config");
        var dataDir = Path.Combine(tempDir, "data");
        var cacheDir = Path.Combine(tempDir, "cache");

        var paths = new AppPaths(configDirOverride: configDir, dataDirOverride: dataDir, isWindowsOverride: false);
        var settings = new SettingsService(paths);
        settings.Update(s => s.ThumbnailCachePath = cacheDir);

        var db = new IconChestDatabase(settings);
        db.Migrate();

        var connFactory = new DatabaseConnectionFactory(db.DatabasePath);
        var folderRepo = new LibraryFolderRepository(connFactory);
        var imageRepo = new ImageRepository(connFactory);
        var cache = new ThumbnailCache(settings);
        var queue = new ThumbnailQueue(connFactory);
        var renderer = new SkiaThumbnailRenderer();
        var provider = new ThumbnailProvider(connFactory, queue, cache, settings);

        try
        {
            var libraryDir = Path.Combine(tempDir, "LargeLib");
            Directory.CreateDirectory(libraryDir);

            // Generate 2,000 files: mix of 16x16 PNG and 32x32 SVG
            const int totalImages = 2000;
            _output.WriteLine($"Generating {totalImages} small images on disk...");
            var genSw = Stopwatch.StartNew();

            var pngSample = ImageTestFixtures.CreatePng(16, 16, transparent: true);
            var svgSample = System.Text.Encoding.UTF8.GetBytes(ImageTestFixtures.CreateSvg(32, 32));

            var hasher = new XxHash128Hasher();
            var records = new List<ImageRecord>(totalImages);
            var folderId = folderRepo.Add(libraryDir);

            for (int i = 0; i < totalImages; i++)
            {
                bool isPng = (i % 2 == 0);
                var fileName = $"img_{i:D5}.{(isPng ? "png" : "svg")}";
                var filePath = Path.Combine(libraryDir, fileName);

                if (isPng)
                {
                    await File.WriteAllBytesAsync(filePath, pngSample);
                }
                else
                {
                    await File.WriteAllBytesAsync(filePath, svgSample);
                }

                records.Add(new ImageRecord
                {
                    FolderId = folderId,
                    Path = filePath,
                    PathKey = filePath,
                    DirectoryPath = libraryDir,
                    DirectoryKey = libraryDir,
                    FileName = fileName,
                    Extension = Path.GetExtension(fileName),
                    Format = isPng ? "png" : "svg",
                    Width = isPng ? 16 : 32,
                    Height = isPng ? 16 : 32,
                    SizeBytes = isPng ? pngSample.Length : svgSample.Length,
                    // Give distinct hash so distinct thumbnail jobs are generated
                    Hash = $"hash_{i:D5}_{Guid.NewGuid():N}",
                    HashAlgo = "xxh128",
                    CreatedUtc = "2026-11-02T12:00:00Z",
                    ModifiedUtc = "2026-11-02T12:00:00Z",
                    AddedUtc = "2026-11-02T12:00:00Z",
                    Title = fileName
                });
            }

            genSw.Stop();
            _output.WriteLine($"Generated {totalImages} files in {genSw.ElapsedMilliseconds} ms.");

            var ids = imageRepo.InsertBatch(records);
            // First 1,000 images (3,000 jobs) with 1 worker
            var batch1Ids = ids.Take(1000).ToList();
            queue.EnqueueForImages(batch1Ids);

            // --- Benchmark 1: 1 Worker (3,000 thumbnail jobs) ---
            settings.Update(s => s.ThumbnailWorkers = 1);
            var worker1 = new ThumbnailWorker(queue, cache, renderer, settings, provider);

            _output.WriteLine("Starting Benchmark 1: 1 Worker processing 3,000 jobs (1,000 images x 3 sizes)...");
            var sw1 = Stopwatch.StartNew();
            worker1.Start();

            var timeout1 = DateTime.UtcNow.AddMinutes(3);
            while (DateTime.UtcNow < timeout1)
            {
                var st = queue.GetStatus();
                if (st.Pending == 0 && st.Running == 0) break;
                await Task.Delay(50);
            }
            sw1.Stop();
            await worker1.StopAsync();

            double throughput1 = 3000.0 / (sw1.ElapsedMilliseconds / 1000.0);
            _output.WriteLine($"1 Worker: Processed 3,000 jobs in {sw1.ElapsedMilliseconds} ms ({throughput1:F1} jobs/sec, {throughput1 / 3.0:F1} images/sec).");

            // --- Benchmark 2: 4 Workers (3,000 thumbnail jobs) ---
            var batch2Ids = ids.Skip(1000).Take(1000).ToList();
            queue.EnqueueForImages(batch2Ids);

            settings.Update(s => s.ThumbnailWorkers = 4);
            var worker4 = new ThumbnailWorker(queue, cache, renderer, settings, provider);

            _output.WriteLine("Starting Benchmark 2: 4 Workers processing 3,000 jobs (1,000 images x 3 sizes)...");
            var sw4 = Stopwatch.StartNew();
            worker4.Start();

            var timeout4 = DateTime.UtcNow.AddMinutes(3);
            while (DateTime.UtcNow < timeout4)
            {
                var st = queue.GetStatus();
                if (st.Pending == 0 && st.Running == 0) break;
                await Task.Delay(50);
            }
            sw4.Stop();
            await worker4.StopAsync();

            double throughput4 = 3000.0 / (sw4.ElapsedMilliseconds / 1000.0);
            _output.WriteLine($"4 Workers: Processed 3,000 jobs in {sw4.ElapsedMilliseconds} ms ({throughput4:F1} jobs/sec, {throughput4 / 3.0:F1} images/sec).");

            var finalStatus = queue.GetStatus();
            Assert.Equal(6000, finalStatus.Done);
            Assert.Equal(0, finalStatus.Pending);
            Assert.Equal(0, finalStatus.Failed);
        }
        finally
        {
            provider.Dispose();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }
}

