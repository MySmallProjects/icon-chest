using Dapper;
using IconChest.Core.Data;
using IconChest.Core.Data.Repositories;
using IconChest.Core.Scanning;
using IconChest.Core.Scanning.Exceptions;
using Xunit;

namespace IconChest.Core.Tests;

public class LibraryFolderTests
{
    private static (IconChestDatabase Db, LibraryFolderService Service, string DbPath, string TempDir) CreateTestContext()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"iconchest-lib-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var dbPath = Path.Combine(tempDir, "test.db");
        var db = new IconChestDatabase(dbPath);
        db.Migrate();

        var connFactory = new DatabaseConnectionFactory(dbPath);
        var repo = new LibraryFolderRepository(connFactory);
        var service = new LibraryFolderService(repo);

        return (db, service, dbPath, tempDir);
    }

    [Fact]
    public void AddFolder_ValidatesAndAddsSuccessfully()
    {
        var (_, service, _, tempDir) = CreateTestContext();
        try
        {
            var folderA = Path.Combine(tempDir, "FolderA");
            Directory.CreateDirectory(folderA);

            var id = service.AddFolder(folderA);
            Assert.True(id > 0);

            var folder = service.GetById(id);
            Assert.NotNull(folder);
            Assert.Equal(Path.GetFullPath(folderA), folder.Path);
            Assert.True(folder.Enabled);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void AddFolder_RejectsNonExistentPath()
    {
        var (_, service, _, tempDir) = CreateTestContext();
        try
        {
            var nonExistent = Path.Combine(tempDir, "DoesNot_Exist_123");
            Assert.Throws<FolderNotFoundException>(() => service.AddFolder(nonExistent));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void AddFolder_RejectsFileInsteadOfDirectory()
    {
        var (_, service, _, tempDir) = CreateTestContext();
        try
        {
            var filePath = Path.Combine(tempDir, "file.txt");
            File.WriteAllText(filePath, "hello");

            Assert.Throws<NotADirectoryException>(() => service.AddFolder(filePath));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void AddFolder_RejectsDuplicateFolder()
    {
        var (_, service, _, tempDir) = CreateTestContext();
        try
        {
            var folder = Path.Combine(tempDir, "MyFolder");
            Directory.CreateDirectory(folder);

            service.AddFolder(folder);
            Assert.Throws<DuplicateFolderException>(() => service.AddFolder(folder));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void AddFolder_RejectsNestedFolders_BothDirections()
    {
        var (_, service, _, tempDir) = CreateTestContext();
        try
        {
            var parent = Path.Combine(tempDir, "Parent");
            var child = Path.Combine(parent, "Child");
            Directory.CreateDirectory(child);

            // Add parent first
            service.AddFolder(parent);

            // Adding child must fail (candidate inside existing parent)
            Assert.Throws<NestedFolderException>(() => service.AddFolder(child));

            // Separate context: add child first
            var (_, service2, _, tempDir2) = CreateTestContext();
            try
            {
                var parent2 = Path.Combine(tempDir2, "Parent2");
                var child2 = Path.Combine(parent2, "Child2");
                Directory.CreateDirectory(child2);

                service2.AddFolder(child2);

                // Adding parent must fail (candidate contains existing child)
                Assert.Throws<NestedFolderException>(() => service2.AddFolder(parent2));
            }
            finally
            {
                if (Directory.Exists(tempDir2)) Directory.Delete(tempDir2, recursive: true);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void RemoveFolder_CascadesToImagesAndTagLinks_KeepsTags_NeverTouchesDiskFiles()
    {
        var (db, service, _, tempDir) = CreateTestContext();
        try
        {
            var folder = Path.Combine(tempDir, "ImagesFolder");
            Directory.CreateDirectory(folder);
            var imageFile = Path.Combine(folder, "icon.png");
            File.WriteAllText(imageFile, "dummy image content");

            var folderId = service.AddFolder(folder);

            // Seed an image record, tag, and image_tag link in DB
            using (var conn = db.OpenConnection())
            {
                var imageId = conn.QuerySingle<long>(@"
                    INSERT INTO image (
                        folder_id, path, path_key, directory_path, directory_key,
                        file_name, extension, format, width, height, size_bytes,
                        hash, hash_algo, created_utc, modified_utc, added_utc, title
                    ) VALUES (
                        @folderId, @imageFile, @imageFile, @folder, @folder,
                        'icon.png', '.png', 'png', 32, 32, 100,
                        'hash1', 'xxh128', '2026-11-01', '2026-11-01', '2026-11-01', 'Icon'
                    ) RETURNING id;", new { folderId, imageFile, folder });

                var tagId = conn.QuerySingle<long>("INSERT INTO tag (name) VALUES ('Nature') RETURNING id;");
                conn.Execute("INSERT INTO image_tag (image_id, tag_id) VALUES (@imageId, @tagId);", new { imageId, tagId });
            }

            // Remove the folder
            service.RemoveFolder(folderId);

            // Verify cascading deletes in DB
            using (var conn = db.OpenConnection())
            {
                Assert.Equal(0, conn.ExecuteScalar<int>("SELECT COUNT(*) FROM library_folder WHERE id = @folderId;", new { folderId }));
                Assert.Equal(0, conn.ExecuteScalar<int>("SELECT COUNT(*) FROM image WHERE folder_id = @folderId;", new { folderId }));
                Assert.Equal(0, conn.ExecuteScalar<int>("SELECT COUNT(*) FROM image_tag;"));
                Assert.Equal(1, conn.ExecuteScalar<int>("SELECT COUNT(*) FROM tag WHERE name = 'Nature';")); // Tag kept
            }

            // Verify original file on disk is UNTOUCHED
            Assert.True(File.Exists(imageFile), "File on disk must never be deleted!");
            Assert.True(Directory.Exists(folder), "Folder on disk must never be deleted!");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }
}

