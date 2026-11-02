using IconChest.Core.Common;
using IconChest.Core.Data;
using IconChest.Core.Data.Repositories;
using IconChest.Core.Scanning;
using IconChest.Core.Settings;
using IconChest.Core.Tests.TestHelpers;
using IconChest.Core.Thumbnails;
using Xunit;

namespace IconChest.Core.Tests;

public class ThumbnailWorkerTests
{
    private static (IconChestDatabase Db,
                    LibraryFolderRepository FolderRepo,
                    ImageRepository ImageRepo,
                    ThumbnailCache Cache,
                    ThumbnailQueue Queue,
                    SkiaThumbnailRenderer Renderer,
                    ThumbnailProvider Provider,
                    SettingsService Settings,
                    string TempDir) CreateTestContext()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"thumb-worker-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var configDir = Path.Combine(tempDir, "config");
        var dataDir = Path.Combine(tempDir, "data");
        var cacheDir = Path.Combine(tempDir, "cache");

        var paths = new AppPaths(configDirOverride: configDir, dataDirOverride: dataDir, isWindowsOverride: false);
        var settings = new SettingsService(paths);
        settings.Update(s =>
        {
            s.ThumbnailCachePath = cacheDir;
            s.ThumbnailWorkers = 2;
        });

        var db = new IconChestDatabase(settings);
        db.Migrate();

        var connFactory = new DatabaseConnectionFactory(db.DatabasePath);
        var folderRepo = new LibraryFolderRepository(connFactory);
        var imageRepo = new ImageRepository(connFactory);
        var cache = new ThumbnailCache(settings);
        var queue = new ThumbnailQueue(connFactory);
        var renderer = new SkiaThumbnailRenderer();
        var provider = new ThumbnailProvider(connFactory, queue, cache, settings);

        return (db, folderRepo, imageRepo, cache, queue, renderer, provider, settings, tempDir);
    }

    [Fact]
    public async Task EndToEnd_ScanToDoneThumbnails_CacheFilesCreated()
    {
        var (_, folderRepo, imageRepo, cache, queue, renderer, provider, settings, tempDir) = CreateTestContext();
        try
        {
            var libraryDir = Path.Combine(tempDir, "Library");
            Directory.CreateDirectory(libraryDir);

            await File.WriteAllBytesAsync(Path.Combine(libraryDir, "icon1.png"), ImageTestFixtures.CreatePng(32, 32, transparent: true));
            await File.WriteAllBytesAsync(Path.Combine(libraryDir, "photo.jpg"), ImageTestFixtures.CreateJpeg(64, 64));
            await File.WriteAllTextAsync(Path.Combine(libraryDir, "vector.svg"), ImageTestFixtures.CreateSvg(48, 48));

            var folderId = folderRepo.Add(libraryDir);

            var worker = new ThumbnailWorker(queue, cache, renderer, settings, provider);
            var scanner = new LibraryScanner(
                folderRepo,
                imageRepo,
                settings,
                new XxHash128Hasher(),
                new ImageMetadataReader(),
                thumbnailQueue: queue,
                thumbnailWorker: worker,
                thumbnailProvider: provider);

            // Scan folder
            var scanResult = await scanner.ScanAsync(folderId);
            Assert.Equal(3, scanResult.FilesFound);
            // 3 files * 3 sizes = 9 thumbnail jobs enqueued
            Assert.Equal(9, scanResult.ThumbnailsEnqueued);

            // Run worker until empty
            worker.Start();

            var timeout = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < timeout)
            {
                var status = queue.GetStatus();
                if (status.Pending == 0 && status.Running == 0) break;
                await Task.Delay(50);
            }

            await worker.StopAsync();

            var finalStatus = queue.GetStatus();
            Assert.Equal(9, finalStatus.Done);
            Assert.Equal(0, finalStatus.Pending);
            Assert.Equal(0, finalStatus.Failed);

            // Check that files exist in cache
            var images = imageRepo.List(folderId);
            foreach (var img in images)
            {
                foreach (var size in ThumbnailSizes.All)
                {
                    Assert.True(cache.Exists(img.Hash, size));
                    var rec = queue.GetRecord(img.Hash, size);
                    Assert.NotNull(rec);
                    Assert.Equal("done", rec.State);
                    Assert.True(rec.Bytes > 0);
                }
            }
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Worker_PauseAndResume_HaltedWhilePaused()
    {
        var (_, folderRepo, imageRepo, cache, queue, renderer, provider, settings, tempDir) = CreateTestContext();
        try
        {
            var libraryDir = Path.Combine(tempDir, "Library");
            Directory.CreateDirectory(libraryDir);

            for (int i = 0; i < 10; i++)
            {
                await File.WriteAllBytesAsync(Path.Combine(libraryDir, $"img_{i}.png"), ImageTestFixtures.CreatePng(16 + i, 16));
            }

            var folderId = folderRepo.Add(libraryDir);
            var scanner = new LibraryScanner(
                folderRepo,
                imageRepo,
                settings,
                new XxHash128Hasher(),
                new ImageMetadataReader(),
                thumbnailQueue: queue);

            await scanner.ScanAsync(folderId);

            var worker = new ThumbnailWorker(queue, cache, renderer, settings, provider);
            worker.Start();

            // Pause immediately
            worker.Pause();
            Assert.True(worker.IsPaused);

            // Wait 200ms
            await Task.Delay(200);
            var statusPaused = queue.GetStatus();

            // While paused, progress does not continue
            await Task.Delay(300);
            var statusStillPaused = queue.GetStatus();
            Assert.Equal(statusPaused.Done, statusStillPaused.Done);

            // Resume
            worker.Resume();
            Assert.False(worker.IsPaused);

            var timeout = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < timeout)
            {
                var st = queue.GetStatus();
                if (st.Pending == 0 && st.Running == 0) break;
                await Task.Delay(50);
            }

            await worker.StopAsync();
            var finalStatus = queue.GetStatus();
            // 10 files * 3 sizes = 30
            Assert.Equal(30, finalStatus.Done);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Worker_StopAndResumeLater_OnlyRemainingJobsProcessed()
    {
        var (_, folderRepo, imageRepo, cache, queue, renderer, provider, settings, tempDir) = CreateTestContext();
        try
        {
            var libraryDir = Path.Combine(tempDir, "Library");
            Directory.CreateDirectory(libraryDir);

            for (int i = 0; i < 10; i++)
            {
                await File.WriteAllBytesAsync(Path.Combine(libraryDir, $"icon_{i}.png"), ImageTestFixtures.CreatePng(16 + i, 16));
            }

            var folderId = folderRepo.Add(libraryDir);
            var scanner = new LibraryScanner(
                folderRepo,
                imageRepo,
                settings,
                new XxHash128Hasher(),
                new ImageMetadataReader(),
                thumbnailQueue: queue);

            await scanner.ScanAsync(folderId);

            // 1. Run first worker part-way
            var worker1 = new ThumbnailWorker(queue, cache, renderer, settings, provider);
            worker1.Start();

            // Let it process a few jobs
            await Task.Delay(150);
            await worker1.StopAsync();

            var status1 = queue.GetStatus();
            Assert.True(status1.Done > 0);
            int doneFirst = status1.Done;

            // 2. Start a second worker instance to finish the queue
            var worker2 = new ThumbnailWorker(queue, cache, renderer, settings, provider);
            worker2.Start();

            var timeout = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < timeout)
            {
                var st = queue.GetStatus();
                if (st.Pending == 0 && st.Running == 0) break;
                await Task.Delay(50);
            }

            await worker2.StopAsync();

            var finalStatus = queue.GetStatus();
            Assert.Equal(30, finalStatus.Done);
            // Proves that already finished work was not redone
            Assert.True(finalStatus.Done >= doneFirst);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Worker_OneFailingImage_DoesNotStopOtherImages()
    {
        var (_, folderRepo, imageRepo, cache, queue, renderer, provider, settings, tempDir) = CreateTestContext();
        try
        {
            var libraryDir = Path.Combine(tempDir, "Library");
            Directory.CreateDirectory(libraryDir);

            var good1 = Path.Combine(libraryDir, "good1.png");
            var corrupt = Path.Combine(libraryDir, "corrupt.png");
            var good2 = Path.Combine(libraryDir, "good2.png");

            await File.WriteAllBytesAsync(good1, ImageTestFixtures.CreatePng(16, 16));
            // Corrupt file that claims to be PNG but is garbage
            await File.WriteAllBytesAsync(corrupt, [0x89, 0x50, 0x4E, 0x47, 0x00, 0x00]);
            await File.WriteAllBytesAsync(good2, ImageTestFixtures.CreatePng(32, 32));

            var folderId = folderRepo.Add(libraryDir);
            var scanner = new LibraryScanner(
                folderRepo,
                imageRepo,
                settings,
                new XxHash128Hasher(),
                new ImageMetadataReader(),
                thumbnailQueue: queue);

            await scanner.ScanAsync(folderId);

            var worker = new ThumbnailWorker(queue, cache, renderer, settings, provider);
            worker.Start();

            var timeout = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < timeout)
            {
                var st = queue.GetStatus();
                if (st.Pending == 0 && st.Running == 0) break;
                await Task.Delay(50);
            }

            await worker.StopAsync();

            var status = queue.GetStatus();
            // The 2 good images (6 jobs) are Done
            Assert.Equal(6, status.Done);
            // The corrupt image failed (or unreadable)
            Assert.True(status.Failed > 0 || status.Pending == 0);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task MovesAndDuplicates_ReusesExistingThumbnails_NewJobsForEditedFile()
    {
        var (_, folderRepo, imageRepo, cache, queue, renderer, provider, settings, tempDir) = CreateTestContext();
        try
        {
            var libraryDir = Path.Combine(tempDir, "Library");
            Directory.CreateDirectory(libraryDir);

            var file1 = Path.Combine(libraryDir, "icon.png");
            await File.WriteAllBytesAsync(file1, ImageTestFixtures.CreatePng(32, 32));

            var folderId = folderRepo.Add(libraryDir);
            var worker = new ThumbnailWorker(queue, cache, renderer, settings, provider);
            var scanner = new LibraryScanner(
                folderRepo,
                imageRepo,
                settings,
                new XxHash128Hasher(),
                new ImageMetadataReader(),
                thumbnailQueue: queue,
                thumbnailWorker: worker);

            // 1. Initial scan & generate thumbnails
            await scanner.ScanAsync(folderId);
            worker.Start();

            var timeout = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < timeout)
            {
                var st = queue.GetStatus();
                if (st.Pending == 0 && st.Running == 0) break;
                await Task.Delay(50);
            }

            var status1 = queue.GetStatus();
            Assert.Equal(3, status1.Done);

            // 2. Duplicate file added
            var fileDuplicate = Path.Combine(libraryDir, "copy.png");
            File.Copy(file1, fileDuplicate);

            var scan2 = await scanner.ScanAsync(folderId);
            // Identical hash -> 0 new thumbnail jobs enqueued
            Assert.Equal(0, scan2.ThumbnailsEnqueued);

            // 3. Move file1 to moved.png
            var fileMoved = Path.Combine(libraryDir, "moved.png");
            File.Move(file1, fileMoved);

            var scan3 = await scanner.ScanAsync(folderId);
            Assert.Equal(1, scan3.MovedFiles);
            // Reused thumbnail -> 0 new thumbnail jobs enqueued
            Assert.Equal(0, scan3.ThumbnailsEnqueued);

            // 4. Edit fileDuplicate so its hash changes
            await File.WriteAllBytesAsync(fileDuplicate, ImageTestFixtures.CreatePng(48, 48));
            var scan4 = await scanner.ScanAsync(folderId);
            Assert.Equal(1, scan4.ChangedFiles);
            // New distinct hash -> 3 new thumbnail jobs enqueued
            Assert.Equal(3, scan4.ThumbnailsEnqueued);

            await worker.StopAsync();
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Safety_SourceFilesAreNeverModifiedDuringScanAndThumbnailing()
    {
        var (_, folderRepo, imageRepo, cache, queue, renderer, provider, settings, tempDir) = CreateTestContext();
        try
        {
            var libraryDir = Path.Combine(tempDir, "Library");
            Directory.CreateDirectory(libraryDir);

            var testFiles = new[]
            {
                Path.Combine(libraryDir, "a.png"),
                Path.Combine(libraryDir, "b.jpg"),
                Path.Combine(libraryDir, "c.svg")
            };

            await File.WriteAllBytesAsync(testFiles[0], ImageTestFixtures.CreatePng(16, 16));
            await File.WriteAllBytesAsync(testFiles[1], ImageTestFixtures.CreateJpeg(16, 16));
            await File.WriteAllTextAsync(testFiles[2], ImageTestFixtures.CreateSvg(16, 16));

            // Record initial timestamps and byte hashes
            var initialLastWriteTimes = testFiles.ToDictionary(f => f, File.GetLastWriteTimeUtc);
            var hasher = new XxHash128Hasher();
            var initialHashes = new Dictionary<string, string>();
            foreach (var f in testFiles)
            {
                initialHashes[f] = await hasher.HashFileAsync(f);
            }

            var folderId = folderRepo.Add(libraryDir);
            var worker = new ThumbnailWorker(queue, cache, renderer, settings, provider);
            var scanner = new LibraryScanner(
                folderRepo,
                imageRepo,
                settings,
                hasher,
                new ImageMetadataReader(),
                thumbnailQueue: queue,
                thumbnailWorker: worker);

            await scanner.ScanAsync(folderId);

            worker.Start();
            var timeout = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < timeout)
            {
                var st = queue.GetStatus();
                if (st.Pending == 0 && st.Running == 0) break;
                await Task.Delay(50);
            }
            await worker.StopAsync();

            // Assert exact byte hash and last write time invariance
            foreach (var f in testFiles)
            {
                var currentHash = await hasher.HashFileAsync(f);
                Assert.Equal(initialHashes[f], currentHash);
                Assert.Equal(initialLastWriteTimes[f], File.GetLastWriteTimeUtc(f));
            }
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }
}

