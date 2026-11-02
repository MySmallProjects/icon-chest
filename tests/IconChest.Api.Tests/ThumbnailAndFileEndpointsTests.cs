using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using IconChest.Api.Tests.TestHelpers;
using IconChest.Core.Data;
using IconChest.Core.Data.Models;
using IconChest.Core.Data.Repositories;
using IconChest.Core.Scanning;
using IconChest.Core.Settings;
using IconChest.Core.Thumbnails;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IconChest.Api.Tests;

[Collection("ApiCollection")]
public class ThumbnailAndFileEndpointsTests
{
    private readonly IconChestApiFactory _factory;

    public ThumbnailAndFileEndpointsTests(IconChestApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Thumbnail_BadSize_Returns400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var res = await client.GetAsync("/api/images/1/thumbnail?size=100");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Thumbnail_NonExistentImage_Returns404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var res = await client.GetAsync("/api/images/999999/thumbnail?size=128");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task File_NonExistentImage_Returns404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var res = await client.GetAsync("/api/images/999999/file");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Thumbnail_Ready_Returns200WithETag_AndSupports304()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        // 1. Setup an image record and physical cached thumbnail
        var folderRepo = _factory.Services.GetRequiredService<ILibraryFolderRepository>();
        var imageRepo = _factory.Services.GetRequiredService<IImageRepository>();
        var cache = _factory.Services.GetRequiredService<IThumbnailCache>();

        var libDir = Path.Combine(_factory.TempDir, "ThumbLib");
        Directory.CreateDirectory(libDir);
        long folderId = folderRepo.Add(libDir);

        string hash = "thumbhash" + Guid.NewGuid().ToString("N");
        var imgPath = Path.Combine(libDir, "sample.png");
        await File.WriteAllBytesAsync(imgPath, [1, 2, 3, 4]);

        var analysis = PathKeyProvider.Default.Analyze(imgPath);
        var rec = new ImageRecord
        {
            FolderId = folderId,
            Path = imgPath,
            PathKey = analysis.PathKey,
            DirectoryPath = analysis.DirectoryPath,
            DirectoryKey = analysis.DirectoryKey,
            FileName = analysis.FileName,
            Extension = analysis.Extension,
            Format = "png",
            Hash = hash,
            SizeBytes = 4,
            Width = 100,
            Height = 100,
            CreatedUtc = DateTime.UtcNow.ToString("O"),
            ModifiedUtc = DateTime.UtcNow.ToString("O"),
            AddedUtc = DateTime.UtcNow.ToString("O")
        };
        long imgId = imageRepo.InsertBatch([rec])[0];

        // Write a synthetic cached thumbnail
        byte[] fakeWebp = Encoding.UTF8.GetBytes("RIFFfakeWEBPdata");
        await cache.WriteAsync(hash, 128, fakeWebp);

        var connFactory = _factory.Services.GetRequiredService<IDatabaseConnectionFactory>();
        using (var conn = connFactory.CreateAndOpenConnection())
        {
            Dapper.SqlMapper.Execute(conn, @"
                INSERT INTO thumbnail (hash, size, state, priority, attempts, bytes, width, height, created_utc, updated_utc, last_accessed_utc)
                VALUES (@hash, 128, 'done', 0, 0, @bytes, 128, 128, @now, @now, @now);",
                new { hash, bytes = fakeWebp.Length, now = DateTime.UtcNow.ToString("O") });
        }

        // 2. Request thumbnail
        var res = await client.GetAsync($"/api/images/{imgId}/thumbnail?size=128");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("image/webp", res.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(res.Headers.ETag);
        string etag = res.Headers.ETag.ToString();

        // 3. Conditional request with If-None-Match returns 304 Not Modified
        var req304 = new HttpRequestMessage(HttpMethod.Get, $"/api/images/{imgId}/thumbnail?size=128");
        req304.Headers.TryAddWithoutValidation("If-None-Match", etag);
        var res304 = await client.SendAsync(req304);
        Assert.Equal(HttpStatusCode.NotModified, res304.StatusCode);
    }

    [Fact]
    public async Task File_AddedImage_SupportsRangeRequestsAndSvgCsp()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var folderRepo = _factory.Services.GetRequiredService<ILibraryFolderRepository>();
        var imageRepo = _factory.Services.GetRequiredService<IImageRepository>();

        var libDir = Path.Combine(_factory.TempDir, "SvgLibDirect");
        Directory.CreateDirectory(libDir);
        long folderId = folderRepo.Add(libDir);

        var svgPath = Path.Combine(libDir, "vector.svg");
        const string svgContent = "<svg xmlns='http://www.w3.org/2000/svg' width='100' height='100'><circle cx='50' cy='50' r='40'/></svg>";
        await File.WriteAllTextAsync(svgPath, svgContent);

        var analysis = PathKeyProvider.Default.Analyze(svgPath);
        var rec = new ImageRecord
        {
            FolderId = folderId,
            Path = svgPath,
            PathKey = analysis.PathKey,
            DirectoryPath = analysis.DirectoryPath,
            DirectoryKey = analysis.DirectoryKey,
            FileName = analysis.FileName,
            Extension = analysis.Extension,
            Format = "svg",
            Hash = "svghash" + Guid.NewGuid().ToString("N"),
            SizeBytes = Encoding.UTF8.GetByteCount(svgContent),
            Width = 100,
            Height = 100,
            CreatedUtc = DateTime.UtcNow.ToString("O"),
            ModifiedUtc = DateTime.UtcNow.ToString("O"),
            AddedUtc = DateTime.UtcNow.ToString("O")
        };
        long imgId = imageRepo.InsertBatch([rec])[0];

        // 1. Test SVG CSP header
        var fileRes = await client.GetAsync($"/api/images/{imgId}/file");
        Assert.Equal(HttpStatusCode.OK, fileRes.StatusCode);
        Assert.True(fileRes.Headers.Contains("Content-Security-Policy"));
        var csp = string.Join(" ", fileRes.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("default-src 'none'", csp);

        // 2. Test Range Request (Byte range: bytes=0-10)
        var rangeReq = new HttpRequestMessage(HttpMethod.Get, $"/api/images/{imgId}/file");
        rangeReq.Headers.Range = new RangeHeaderValue(0, 10);

        var rangeRes = await client.SendAsync(rangeReq);
        Assert.Equal(HttpStatusCode.PartialContent, rangeRes.StatusCode);
        Assert.NotNull(rangeRes.Content.Headers.ContentRange);
        var bytes = await rangeRes.Content.ReadAsByteArrayAsync();
        Assert.Equal(11, bytes.Length); // 0 through 10 inclusive = 11 bytes
    }
}
