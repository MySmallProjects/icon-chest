using IconChest.Core.Scanning;
using IconChest.Core.Tests.TestHelpers;
using Xunit;

namespace IconChest.Core.Tests;

public class MetadataTests
{
    private readonly ImageMetadataReader _reader = new(TimeZoneInfo.Utc);

    [Fact]
    public async Task Metadata_Formats_DecodesCorrectly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"meta-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var pngPath = Path.Combine(tempDir, "test.png");
            var jpgPath = Path.Combine(tempDir, "test.jpg");
            var gifPath = Path.Combine(tempDir, "test.gif");
            var webpPath = Path.Combine(tempDir, "test.webp");
            var bmpPath = Path.Combine(tempDir, "test.bmp");
            var icoPath = Path.Combine(tempDir, "test.ico");
            var svgPath = Path.Combine(tempDir, "test.svg");

            await File.WriteAllBytesAsync(pngPath, ImageTestFixtures.CreatePng(64, 48));
            await File.WriteAllBytesAsync(jpgPath, ImageTestFixtures.CreateJpeg(100, 80));
            await File.WriteAllBytesAsync(gifPath, ImageTestFixtures.CreateGif(16, 16));
            await File.WriteAllBytesAsync(webpPath, ImageTestFixtures.CreateWebp(30, 20));
            await File.WriteAllBytesAsync(bmpPath, ImageTestFixtures.CreateBmp(10, 10));
            await File.WriteAllBytesAsync(icoPath, ImageTestFixtures.CreateIco(16, 16));
            await File.WriteAllTextAsync(svgPath, ImageTestFixtures.CreateSvg(120, 90));

            var png = await _reader.ReadMetadataAsync(pngPath);
            Assert.Equal("png", png.Format);
            Assert.Equal(64, png.Width);
            Assert.Equal(48, png.Height);
            Assert.Equal("ok", png.Status);

            var jpg = await _reader.ReadMetadataAsync(jpgPath);
            Assert.Equal("jpg", jpg.Format);
            Assert.Equal(100, jpg.Width);
            Assert.Equal(80, jpg.Height);

            var gif = await _reader.ReadMetadataAsync(gifPath);
            Assert.Equal("gif", gif.Format);
            Assert.Equal(16, gif.Width);
            Assert.Equal(16, gif.Height);

            var webp = await _reader.ReadMetadataAsync(webpPath);
            Assert.Equal("webp", webp.Format);
            Assert.Equal(30, webp.Width);
            Assert.Equal(20, webp.Height);

            var bmp = await _reader.ReadMetadataAsync(bmpPath);
            Assert.Equal("bmp", bmp.Format);
            Assert.Equal(10, bmp.Width);
            Assert.Equal(10, bmp.Height);

            var ico = await _reader.ReadMetadataAsync(icoPath);
            Assert.Equal("ico", ico.Format);
            Assert.Equal(16, ico.Width);
            Assert.Equal(16, ico.Height);

            var svg = await _reader.ReadMetadataAsync(svgPath);
            Assert.Equal("svg", svg.Format);
            Assert.Equal(120, svg.Width);
            Assert.Equal(90, svg.Height);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Metadata_WrongExtension_SniffsRealFormat()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"meta-wrong-ext-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            // PNG bytes saved with .jpg extension
            var misnamed = Path.Combine(tempDir, "photo.jpg");
            await File.WriteAllBytesAsync(misnamed, ImageTestFixtures.CreatePng(50, 50));

            var meta = await _reader.ReadMetadataAsync(misnamed);
            Assert.Equal("png", meta.Format);
            Assert.Equal(50, meta.Width);
            Assert.Equal(50, meta.Height);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Metadata_SvgUnitsAndViewBoxFallback()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"meta-svg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            // SVG in inches (1 in = 96 px)
            var svgInches = Path.Combine(tempDir, "inches.svg");
            await File.WriteAllTextAsync(svgInches, ImageTestFixtures.CreateSvg(2, 3, unit: "in"));
            var metaInches = await _reader.ReadMetadataAsync(svgInches);
            Assert.Equal(192, metaInches.Width);
            Assert.Equal(288, metaInches.Height);

            // SVG with viewBox only (no width/height)
            var svgViewBox = Path.Combine(tempDir, "viewbox.svg");
            await File.WriteAllTextAsync(svgViewBox, ImageTestFixtures.CreateSvg(0, 0, viewBox: "0 0 200 150"));
            var metaVb = await _reader.ReadMetadataAsync(svgViewBox);
            Assert.Equal(200, metaVb.Width);
            Assert.Equal(150, metaVb.Height);
            Assert.Equal("ok", metaVb.Status);

            // SVG with neither
            var svgNoDim = Path.Combine(tempDir, "nodim.svg");
            await File.WriteAllTextAsync(svgNoDim, "<svg xmlns=\"http://www.w3.org/2000/svg\"><circle r=\"10\"/></svg>");
            var metaNoDim = await _reader.ReadMetadataAsync(svgNoDim);
            Assert.Null(metaNoDim.Width);
            Assert.Null(metaNoDim.Height);
            Assert.Equal("no_dimensions", metaNoDim.Status);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Metadata_Exif_ReadsOrientationAndConvertsTimezone()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"meta-exif-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            // Orientation 6: rotated 90 CW, width & height (40, 20) should be swapped to (20, 40)
            var jpgExif = Path.Combine(tempDir, "exif.jpg");
            var bytes = ImageTestFixtures.CreateJpegWithExif(width: 40, height: 20, orientation: 6, dateTaken: "2026:10:30 14:00:00");
            await File.WriteAllBytesAsync(jpgExif, bytes);

            // Inject timezone with +2 hours offset
            var customTz = TimeZoneInfo.CreateCustomTimeZone("Test+2", TimeSpan.FromHours(2), "Test+2", "Test+2");
            var readerWithTz = new ImageMetadataReader(customTz);

            var meta = await readerWithTz.ReadMetadataAsync(jpgExif);

            Assert.Equal("jpg", meta.Format);
            Assert.Equal(20, meta.Width); // Swapped because orientation 6
            Assert.Equal(40, meta.Height);

            Assert.NotNull(meta.ExifDateUtc);
            // 14:00 in +02:00 = 12:00 UTC
            Assert.Contains("2026-10-30T12:00:00Z", meta.ExifDateUtc);
            Assert.NotNull(meta.ExifJson);
            Assert.Contains("Canon", meta.ExifJson);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Metadata_Transparency_TrueFalseAndNullCases()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"meta-trans-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var opaquePng = Path.Combine(tempDir, "opaque.png");
            var transPng = Path.Combine(tempDir, "trans.png");
            var jpg = Path.Combine(tempDir, "photo.jpg");

            await File.WriteAllBytesAsync(opaquePng, ImageTestFixtures.CreatePng(16, 16, transparent: false));
            await File.WriteAllBytesAsync(transPng, ImageTestFixtures.CreatePng(16, 16, transparent: true));
            await File.WriteAllBytesAsync(jpg, ImageTestFixtures.CreateJpeg(16, 16));

            var metaOpaque = await _reader.ReadMetadataAsync(opaquePng);
            var metaTrans = await _reader.ReadMetadataAsync(transPng);
            var metaJpg = await _reader.ReadMetadataAsync(jpg);

            Assert.False(metaOpaque.HasTransparency);
            Assert.True(metaTrans.HasTransparency);
            Assert.Null(metaJpg.HasTransparency); // Null for JPEG
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Metadata_CorruptAndZeroByte_ReturnsUnreadableWithoutThrowing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"meta-err-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var zeroByte = Path.Combine(tempDir, "zero.png");
            await File.WriteAllBytesAsync(zeroByte, Array.Empty<byte>());

            var corrupt = Path.Combine(tempDir, "corrupt.jpg");
            await File.WriteAllBytesAsync(corrupt, new byte[] { 0xFF, 0xD8, 0xFF, 0x00, 0x12 }); // Truncated header

            var metaZero = await _reader.ReadMetadataAsync(zeroByte);
            Assert.Equal("unreadable", metaZero.Status);

            var metaCorrupt = await _reader.ReadMetadataAsync(corrupt);
            Assert.Equal("unreadable", metaCorrupt.Status);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("my_icon-file_name.png", "my icon file name")]
    [InlineData("__test---icon___.jpg", "test icon")]
    [InlineData("simple.svg", "simple")]
    public void DeriveDefaultTitle_CleansFileNameCorrectly(string fileName, string expected)
    {
        var title = _reader.DeriveDefaultTitle(fileName);
        Assert.Equal(expected, title);
    }
}

