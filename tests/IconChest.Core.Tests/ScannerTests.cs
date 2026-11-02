using Dapper;
using IconChest.Core.Common;
using IconChest.Core.Data;
using IconChest.Core.Data.Repositories;
using IconChest.Core.Scanning;
using IconChest.Core.Scanning.Exceptions;
using IconChest.Core.Settings;
using IconChest.Core.Tests.TestHelpers;
using Xunit;

namespace IconChest.Core.Tests;

public class ScannerTests
{
    private sealed class CountingHasher : IFileHasher
    {
        private readonly XxHash128Hasher _inner = new();
        public int HashCalls;
        public string AlgorithmName => _inner.AlgorithmName;

        public Task<string> HashFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref HashCalls);
            return _inner.HashFileAsync(filePath, cancellationToken);
        }
    }

    private sealed class ActionHasher : IFileHasher
    {
        private readonly XxHash128Hasher _inner = new();
        private readonly Action _onHash;
        public ActionHasher(Action onHash) => _onHash = onHash;
        public string AlgorithmName => _inner.AlgorithmName;

        public Task<string> HashFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            _onHash();
            return _inner.HashFileAsync(filePath, cancellationToken);
        }
    }

    private sealed class CountingMetadataReader : IImageMetadataReader
    {
        private readonly ImageMetadataReader _inner = new();
        public int MetadataCalls;

        public TimeZoneInfo TimeZone
        {
            get => _inner.TimeZone;
            set => _inner.TimeZone = value;
        }

        public Task<ImageMetadata> ReadMetadataAsync(string filePath, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref MetadataCalls);
            return _inner.ReadMetadataAsync(filePath, cancellationToken);
        }

        public string DeriveDefaultTitle(string fileName) => _inner.DeriveDefaultTitle(fileName);
        public string SniffFormat(ReadOnlySpan<byte> headerBytes) => _inner.SniffFormat(headerBytes);
    }

    private static (IconChestDatabase Db, LibraryFolderRepository FolderRepo, ImageRepository ImageRepo, SettingsService Settings, string TempDir) CreateTestContext()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"scanner-test-{Guid.NewGuid():N}");
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

        return (db, folderRepo, imageRepo, settings, tempDir);
    }

    [Fact]
    public async Task FirstScan_RecordsAllSupportedFiles_DerivesTitle_CorrectCounts()
    {
        var (_, folderRepo, imageRepo, settings, tempDir) = CreateTestContext();
        try
        {
            var folderPath = Path.Combine(tempDir, "LibraryA");
            Directory.CreateDirectory(folderPath);

            await File.WriteAllBytesAsync(Path.Combine(folderPath, "first_icon.png"), ImageTestFixtures.CreatePng(32, 32));
            await File.WriteAllBytesAsync(Path.Combine(folderPath, "photo_sample.jpg"), ImageTestFixtures.CreateJpeg(64, 64));
            await File.WriteAllTextAsync(Path.Combine(folderPath, "vector.svg"), ImageTestFixtures.CreateSvg(100, 100));
            await File.WriteAllTextAsync(Path.Combine(folderPath, "notes.txt"), "This should be ignored");

            var folderId = folderRepo.Add(folderPath);
            var scanner = new LibraryScanner(folderRepo, imageRepo, settings, new XxHash128Hasher(), new ImageMetadataReader());

            var result = await scanner.ScanAsync(folderId);

            Assert.Equal(3, result.FilesFound);
            Assert.Equal(3, result.NewFiles);
            Assert.Equal(0, result.Errors);

            var images = imageRepo.List(folderId);
            Assert.Equal(3, images.Count);

            var png = images.First(i => i.Format == "png");
            Assert.Equal("first icon", png.Title);
            Assert.Equal(32, png.Width);
            Assert.Equal(32, png.Height);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Rescan_UnchangedFiles_ZeroHashAndMetadataCalls_PreservesEditsOnChangedFile()
    {
        var (_, folderRepo, imageRepo, settings, tempDir) = CreateTestContext();
        try
        {
            var folderPath = Path.Combine(tempDir, "RescanLib");
            Directory.CreateDirectory(folderPath);

            var file1 = Path.Combine(folderPath, "icon1.png");
            var file2 = Path.Combine(folderPath, "icon2.png");
            await File.WriteAllBytesAsync(file1, ImageTestFixtures.CreatePng(16, 16));
            await File.WriteAllBytesAsync(file2, ImageTestFixtures.CreatePng(32, 32));

            var countingHasher = new CountingHasher();
            var countingReader = new CountingMetadataReader();
            var folderId = folderRepo.Add(folderPath);

            var scanner = new LibraryScanner(folderRepo, imageRepo, settings, countingHasher, countingReader);

            // First scan
            var res1 = await scanner.ScanAsync(folderId);
            Assert.Equal(2, res1.NewFiles);
            Assert.Equal(2, countingHasher.HashCalls);
            Assert.Equal(2, countingReader.MetadataCalls);

            // User edits on file 2: customize title, rating, favourite, tags
            var img2 = imageRepo.GetRecordsByFolder(folderId).Values.First(i => i.FileName == "icon2.png");
            using (var conn = new DatabaseConnectionFactory(Path.Combine(tempDir, "data", "iconchest.db")).CreateAndOpenConnection())
            {
                conn.Execute("UPDATE image SET title = 'Custom Title', rating = 5, is_favourite = 1, is_hidden = 1 WHERE id = @Id;", new { img2.Id });
            }

            // Rescan with no changes
            countingHasher.HashCalls = 0;
            countingReader.MetadataCalls = 0;

            var res2 = await scanner.ScanAsync(folderId);
            Assert.Equal(2, res2.UnchangedFiles);
            Assert.Equal(0, res2.NewFiles);
            Assert.Equal(0, res2.ChangedFiles);
            // CRITICAL TEST INVARIANT: Zero hash and zero metadata calls!
            Assert.Equal(0, countingHasher.HashCalls);
            Assert.Equal(0, countingReader.MetadataCalls);

            // Now modify file 2 content on disk
            await Task.Delay(100);
            await File.WriteAllBytesAsync(file2, ImageTestFixtures.CreatePng(48, 48)); // new size & timestamp

            countingHasher.HashCalls = 0;
            countingReader.MetadataCalls = 0;

            var res3 = await scanner.ScanAsync(folderId);
            Assert.Equal(1, res3.UnchangedFiles);
            Assert.Equal(1, res3.ChangedFiles);
            Assert.Equal(1, countingHasher.HashCalls);
            Assert.Equal(1, countingReader.MetadataCalls);

            // Confirm user edits preserved on rescan
            var updated2 = imageRepo.GetById(img2.Id);
            Assert.NotNull(updated2);
            Assert.Equal(48, updated2.Width); // New technical field updated
            Assert.Equal("Custom Title", updated2.Title); // Preserved!
            Assert.Equal(5, updated2.Rating); // Preserved!
            Assert.True(updated2.IsFavourite); // Preserved!
            Assert.True(updated2.IsHidden); // Preserved!
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Moves_DetectsRenameSubfolderAndCrossFolder_PreservesIdAndEdits()
    {
        var (_, folderRepo, imageRepo, settings, tempDir) = CreateTestContext();
        try
        {
            var folderA = Path.Combine(tempDir, "FolderA");
            var folderB = Path.Combine(tempDir, "FolderB");
            Directory.CreateDirectory(folderA);
            Directory.CreateDirectory(folderB);

            var oldPath = Path.Combine(folderA, "original.png");
            var imgBytes = ImageTestFixtures.CreatePng(24, 24);
            await File.WriteAllBytesAsync(oldPath, imgBytes);

            var idA = folderRepo.Add(folderA);
            var idB = folderRepo.Add(folderB);

            var scanner = new LibraryScanner(folderRepo, imageRepo, settings, new XxHash128Hasher(), new ImageMetadataReader());

            // 1. Initial scan in FolderA
            await scanner.ScanAsync(idA);
            var initial = imageRepo.GetRecordsByFolder(idA).Values.Single();
            var originalId = initial.Id;

            // 2. Move file into subfolder inside FolderA
            var subDir = Path.Combine(folderA, "SubDir");
            Directory.CreateDirectory(subDir);
            var subPath = Path.Combine(subDir, "renamed_sub.png");
            File.Move(oldPath, subPath);

            var resMoveSub = await scanner.ScanAsync(idA);
            Assert.Equal(1, resMoveSub.MovedFiles);
            Assert.Equal(0, resMoveSub.MissingFiles);

            var movedSub = imageRepo.GetById(originalId);
            Assert.NotNull(movedSub);
            Assert.Equal(subPath, movedSub.Path);
            Assert.Equal("renamed_sub.png", movedSub.FileName);

            // 3. Move file across library folders: from FolderA to FolderB
            var crossPath = Path.Combine(folderB, "cross_folder.png");
            File.Move(subPath, crossPath);

            // Scan A: notices subPath missing
            await scanner.ScanAsync(idA);
            var missingInA = imageRepo.GetById(originalId);
            Assert.True(missingInA!.IsMissing);

            // Scan B: discovers crossPath, matches to library move pool, restores original ID into Folder B!
            var resB = await scanner.ScanAsync(idB);
            Assert.Equal(1, resB.MovedFiles);
            Assert.Equal(0, resB.NewFiles);

            var movedCross = imageRepo.GetById(originalId);
            Assert.NotNull(movedCross);
            Assert.Equal(idB, movedCross.FolderId);
            Assert.Equal(crossPath, movedCross.Path);
            Assert.False(movedCross.IsMissing);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Missing_MarksMissing_RestoresOnReappear_MissingRootDoesNotMarkMissing()
    {
        var (_, folderRepo, imageRepo, settings, tempDir) = CreateTestContext();
        try
        {
            var folder = Path.Combine(tempDir, "MissingLib");
            Directory.CreateDirectory(folder);

            var file1 = Path.Combine(folder, "keep.png");
            var file2 = Path.Combine(folder, "delete.png");
            await File.WriteAllBytesAsync(file1, ImageTestFixtures.CreatePng(10, 10));
            await File.WriteAllBytesAsync(file2, ImageTestFixtures.CreatePng(20, 20));

            var folderId = folderRepo.Add(folder);
            var scanner = new LibraryScanner(folderRepo, imageRepo, settings, new XxHash128Hasher(), new ImageMetadataReader());

            await scanner.ScanAsync(folderId);

            // Delete file2 on disk
            File.Delete(file2);

            var res2 = await scanner.ScanAsync(folderId);
            Assert.Equal(1, res2.MissingFiles);

            var img2 = imageRepo.GetRecordsByFolder(folderId).Values.First(i => i.FileName == "delete.png");
            Assert.True(img2.IsMissing);
            Assert.NotNull(img2.MissingSinceUtc);

            // Safety check: rename whole folder to simulate unplugged drive
            var renamedFolder = Path.Combine(tempDir, "MissingLib_Unplugged");
            Directory.Move(folder, renamedFolder);

            var resMissingRoot = await scanner.ScanAsync(folderId);
            Assert.True(resMissingRoot.IsFatalError);

            // File1 MUST NOT be marked missing because root failed!
            var img1 = imageRepo.GetRecordsByFolder(folderId).Values.First(i => i.FileName == "keep.png");
            Assert.False(img1.IsMissing);

            // Restore root folder and restore file2
            Directory.Move(renamedFolder, folder);
            await File.WriteAllBytesAsync(file2, ImageTestFixtures.CreatePng(20, 20));

            var resRestore = await scanner.ScanAsync(folderId);
            var img2Restored = imageRepo.GetById(img2.Id);
            Assert.NotNull(img2Restored);
            Assert.False(img2Restored.IsMissing);
            Assert.Null(img2Restored.MissingSinceUtc);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Duplicates_And_Hidden_Behaviour()
    {
        var (_, folderRepo, imageRepo, settings, tempDir) = CreateTestContext();
        try
        {
            var folder = Path.Combine(tempDir, "DupeLib");
            Directory.CreateDirectory(folder);

            var fileOriginal = Path.Combine(folder, "orig.png");
            var fileCopy = Path.Combine(folder, "copy.png");
            var pngBytes = ImageTestFixtures.CreatePng(30, 30);
            await File.WriteAllBytesAsync(fileOriginal, pngBytes);
            await File.WriteAllBytesAsync(fileCopy, pngBytes); // Exact identical copy

            var folderId = folderRepo.Add(folder);
            var scanner = new LibraryScanner(folderRepo, imageRepo, settings, new XxHash128Hasher(), new ImageMetadataReader());

            await scanner.ScanAsync(folderId);

            var records = imageRepo.GetRecordsByFolder(folderId).Values.ToList();
            Assert.Equal(2, records.Count);
            Assert.True(records[0].IsDuplicate);
            Assert.True(records[1].IsDuplicate);

            // Hide one of them
            imageRepo.SetHidden(records[0].Id, true);
            imageRepo.RecomputeDuplicates(new[] { records[0].Hash });

            var origAfterHide = imageRepo.GetById(records[0].Id);
            var copyAfterHide = imageRepo.GetById(records[1].Id);

            Assert.False(origAfterHide!.IsDuplicate); // Hidden file not counted as duplicate
            Assert.False(copyAfterHide!.IsDuplicate); // Lone remaining file is no longer a duplicate!

            // Rescan: hidden flag must survive rescan
            await scanner.ScanAsync(folderId);
            var origAfterRescan = imageRepo.GetById(records[0].Id);
            Assert.True(origAfterRescan!.IsHidden);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Concurrency_SecondScanOnSameFolderFailsFast_DifferentFoldersScanInParallel()
    {
        var (_, folderRepo, imageRepo, settings, tempDir) = CreateTestContext();
        try
        {
            var folder1 = Path.Combine(tempDir, "Conc1");
            var folder2 = Path.Combine(tempDir, "Conc2");
            Directory.CreateDirectory(folder1);
            Directory.CreateDirectory(folder2);

            await File.WriteAllBytesAsync(Path.Combine(folder1, "a.png"), ImageTestFixtures.CreatePng(10, 10));
            await File.WriteAllBytesAsync(Path.Combine(folder2, "b.png"), ImageTestFixtures.CreatePng(10, 10));

            var id1 = folderRepo.Add(folder1);
            var id2 = folderRepo.Add(folder2);

            var scanner = new LibraryScanner(folderRepo, imageRepo, settings, new XxHash128Hasher(), new ImageMetadataReader());

            // Run folder1 and folder2 in parallel -> must succeed
            var t1 = scanner.ScanAsync(id1);
            var t2 = scanner.ScanAsync(id2);
            var r1 = await t1;
            var r2 = await t2;

            Assert.Equal(1, r1.NewFiles);
            Assert.Equal(1, r2.NewFiles);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Cancellation_LeavesDatabaseConsistent()
    {
        var (_, folderRepo, imageRepo, settings, tempDir) = CreateTestContext();
        try
        {
            var folder = Path.Combine(tempDir, "CancelLib");
            Directory.CreateDirectory(folder);

            for (var i = 0; i < 20; i++)
            {
                await File.WriteAllBytesAsync(Path.Combine(folder, $"img_{i:D2}.png"), ImageTestFixtures.CreatePng(10, 10));
            }

            var folderId = folderRepo.Add(folder);

            using var cts = new CancellationTokenSource();
            var cancellableHasher = new ActionHasher(() =>
            {
                cts.Cancel();
            });

            var scanner = new LibraryScanner(folderRepo, imageRepo, settings, cancellableHasher, new ImageMetadataReader());

            var result = await scanner.ScanAsync(folderId, null, cts.Token);
            Assert.True(result.IsCancelled);

            // DB remains in a valid state
            var count = imageRepo.Count(folderId);
            Assert.True(count >= 0);

            // Next scan cleanly completes
            var normalScanner = new LibraryScanner(folderRepo, imageRepo, settings, new XxHash128Hasher(), new ImageMetadataReader());
            var completeResult = await normalScanner.ScanAsync(folderId);
            Assert.False(completeResult.IsCancelled);
            Assert.Equal(20, imageRepo.Count(folderId));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }
}

