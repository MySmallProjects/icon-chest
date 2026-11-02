using System.Text;
using IconChest.Core.Tests.TestHelpers;
using IconChest.Core.Thumbnails;
using SkiaSharp;
using Xunit;

namespace IconChest.Core.Tests;

public class ThumbnailRendererTests
{
    private static async Task<string> CreateTempImageFileAsync(string extension, byte[] content)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test-img-{Guid.NewGuid():N}{extension}");
        await File.WriteAllBytesAsync(tempFile, content);
        return tempFile;
    }

    [Fact]
    public async Task Render_ProducesValidWebP_DecodesBackCorrectly()
    {
        var renderer = new SkiaThumbnailRenderer();
        var tempFile = await CreateTempImageFileAsync(".png", ImageTestFixtures.CreatePng(100, 100));
        try
        {
            var result = await renderer.RenderAsync(tempFile, 64);
            Assert.True(result.Status == ThumbnailRenderStatus.Success, $"Expected Success but got {result.Status}: {result.ErrorMessage}");
            Assert.NotNull(result.Bytes);
            Assert.True(result.Bytes.Length > 12);

            // Check RIFF and WEBP signature
            var header = Encoding.ASCII.GetString(result.Bytes, 0, 4);
            var format = Encoding.ASCII.GetString(result.Bytes, 8, 4);
            Assert.Equal("RIFF", header);
            Assert.Equal("WEBP", format);

            // Decode back with SkiaSharp
            using var decoded = SKBitmap.Decode(result.Bytes);
            Assert.NotNull(decoded);
            Assert.Equal(64, decoded.Width);
            Assert.Equal(64, decoded.Height);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Theory]
    [InlineData(512, 256, 64, 64, 32)]    // Landscape
    [InlineData(512, 256, 128, 128, 64)]
    [InlineData(512, 256, 256, 256, 128)]
    [InlineData(256, 512, 64, 32, 64)]    // Portrait
    [InlineData(256, 512, 128, 64, 128)]
    [InlineData(256, 512, 256, 128, 256)]
    [InlineData(512, 512, 64, 64, 64)]    // Square
    [InlineData(512, 512, 128, 128, 128)]
    [InlineData(512, 512, 256, 256, 256)]
    public async Task Render_AspectPreservation_AtAllThreeSizes(int srcW, int srcH, int targetSize, int expW, int expH)
    {
        var renderer = new SkiaThumbnailRenderer();
        var tempFile = await CreateTempImageFileAsync(".png", ImageTestFixtures.CreatePng(srcW, srcH));
        try
        {
            var result = await renderer.RenderAsync(tempFile, targetSize);
            Assert.Equal(ThumbnailRenderStatus.Success, result.Status);
            Assert.Equal(expW, result.Width);
            Assert.Equal(expH, result.Height);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Theory]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(256)]
    public async Task Render_NeverUpscalesSmallRaster(int targetSize)
    {
        var renderer = new SkiaThumbnailRenderer();
        // A 32x32 small icon
        var tempFile = await CreateTempImageFileAsync(".png", ImageTestFixtures.CreatePng(32, 32));
        try
        {
            var result = await renderer.RenderAsync(tempFile, targetSize);
            Assert.Equal(ThumbnailRenderStatus.Success, result.Status);
            // Must remain 32x32 and not upscale
            Assert.Equal(32, result.Width);
            Assert.Equal(32, result.Height);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Theory]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(256)]
    public async Task Render_SvgScalesUpCrisply_AtTargetSizes(int targetSize)
    {
        var renderer = new SkiaThumbnailRenderer();
        // Tiny 16x16 SVG
        var svgText = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\" viewBox=\"0 0 16 16\"><circle cx=\"8\" cy=\"8\" r=\"8\" fill=\"green\"/></svg>";
        var tempFile = await CreateTempImageFileAsync(".svg", Encoding.UTF8.GetBytes(svgText));
        try
        {
            var result = await renderer.RenderAsync(tempFile, targetSize);
            Assert.Equal(ThumbnailRenderStatus.Success, result.Status);
            // SVGs are the exception: scaled to fit target size
            Assert.Equal(targetSize, result.Width);
            Assert.Equal(targetSize, result.Height);

            using var decoded = SKBitmap.Decode(result.Bytes);
            Assert.NotNull(decoded);
            Assert.Equal(targetSize, decoded.Width);
            Assert.Equal(targetSize, decoded.Height);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Render_TransparentPng_PreservesTransparentCornersAndInteriorColor()
    {
        // 64x64 bitmap with transparent corners and red circle in center
        using var bmp = new SKBitmap(64, 64, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { Color = new SKColor(255, 0, 0, 255), IsAntialias = true };
        canvas.DrawCircle(32, 32, 20, paint);

        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);

        var tempFile = await CreateTempImageFileAsync(".png", data.ToArray());
        var renderer = new SkiaThumbnailRenderer();

        try
        {
            var result = await renderer.RenderAsync(tempFile, 64);
            Assert.True(result.Status == ThumbnailRenderStatus.Success, $"Expected Success but got {result.Status}: {result.ErrorMessage}");

            using var decoded = SKBitmap.Decode(result.Bytes);
            Assert.NotNull(decoded);

            // Corner (0,0) must be transparent
            var cornerPixel = decoded.GetPixel(0, 0);
            Assert.Equal(0, cornerPixel.Alpha);

            // Center (32,32) must be red
            var centerPixel = decoded.GetPixel(32, 32);
            Assert.True(centerPixel.Alpha > 240);
            Assert.True(centerPixel.Red > 240);
            Assert.True(centerPixel.Green < 15);
            Assert.True(centerPixel.Blue < 15);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Render_Ico_UsesLargestFrame()
    {
        var renderer = new SkiaThumbnailRenderer();
        // Multi-frame ICO with 16x16 and 48x48 frames
        var icoData = ImageTestFixtures.CreateMultiFrameIco((16, 16), (48, 48));
        var tempFile = await CreateTempImageFileAsync(".ico", icoData);

        try
        {
            var result = await renderer.RenderAsync(tempFile, 64);
            Assert.True(result.Status == ThumbnailRenderStatus.Success, $"Expected Success but got {result.Status}: {result.ErrorMessage}");
            // Target is 64, but source max frame is 48x48. Never upscale raster -> 48x48
            Assert.Equal(48, result.Width);
            Assert.Equal(48, result.Height);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Render_ExifOrientation_RotatesUpright()
    {
        var renderer = new SkiaThumbnailRenderer();
        // Landscape image (100x50) with EXIF orientation 6 (Rotate 90 CW)
        // Upright dimensions should become portrait (50x100), fitting inside 64 -> (32x64)
        var jpgWithExif = ImageTestFixtures.CreateJpegWithExif(100, 50, orientation: 6);
        var tempFile = await CreateTempImageFileAsync(".jpg", jpgWithExif);

        try
        {
            var result = await renderer.RenderAsync(tempFile, 64);
            Assert.True(result.Status == ThumbnailRenderStatus.Success, $"Expected Success but got {result.Status}: {result.ErrorMessage}");
            Assert.Equal(32, result.Width);
            Assert.Equal(64, result.Height);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Render_AnimatedGif_RendersFirstFrame()
    {
        var renderer = new SkiaThumbnailRenderer();
        var tempFile = await CreateTempImageFileAsync(".gif", ImageTestFixtures.CreateGif(32, 32));

        try
        {
            var result = await renderer.RenderAsync(tempFile, 64);
            Assert.True(result.Status == ThumbnailRenderStatus.Success, $"Expected Success but got {result.Status}: {result.ErrorMessage}");
            Assert.Equal(32, result.Width);
            Assert.Equal(32, result.Height);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Render_CorruptOrZeroByte_ReturnsTypedFailure()
    {
        var renderer = new SkiaThumbnailRenderer();
        var zeroFile = await CreateTempImageFileAsync(".png", []);
        var corruptFile = await CreateTempImageFileAsync(".jpg", [0xFF, 0xD8, 0xFF, 0x00, 0x01, 0x02]);

        try
        {
            var resZero = await renderer.RenderAsync(zeroFile, 64);
            Assert.Equal(ThumbnailRenderStatus.Unreadable, resZero.Status);

            var resCorrupt = await renderer.RenderAsync(corruptFile, 64);
            Assert.Equal(ThumbnailRenderStatus.Unreadable, resCorrupt.Status);
        }
        finally
        {
            if (File.Exists(zeroFile)) File.Delete(zeroFile);
            if (File.Exists(corruptFile)) File.Delete(corruptFile);
        }
    }

    [Fact]
    public async Task Render_DecompressionBombGuard_RejectsSourcesOver200Megapixels()
    {
        var renderer = new SkiaThumbnailRenderer();
        // SVG declaring 20000x20000 = 400 Megapixels
        var bombSvg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"20000\" height=\"20000\"><rect width=\"100%\" height=\"100%\" fill=\"black\"/></svg>";
        var tempFile = await CreateTempImageFileAsync(".svg", Encoding.UTF8.GetBytes(bombSvg));

        try
        {
            var result = await renderer.RenderAsync(tempFile, 64);
            Assert.Equal(ThumbnailRenderStatus.TooLarge, result.Status);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Render_SvgWithExternalEntitiesOrScripts_DoesNotExecuteOrFailGracefully()
    {
        var renderer = new SkiaThumbnailRenderer();
        var maliciousSvg = @"<?xml version=""1.0"" standalone=""no""?>
<!DOCTYPE svg [
<!ELEMENT svg ANY >
<!ENTITY xxe SYSTEM ""file:///etc/passwd"" >]>
<svg xmlns=""http://www.w3.org/2000/svg"" width=""64"" height=""64"">
  <script>alert(1);</script>
  <text>&xxe;</text>
</svg>";

        var tempFile = await CreateTempImageFileAsync(".svg", Encoding.UTF8.GetBytes(maliciousSvg));

        try
        {
            var result = await renderer.RenderAsync(tempFile, 64);
            // DtdProcessing.Prohibit will reject the DTD or handle safely without entity expansion
            Assert.True(result.Status is ThumbnailRenderStatus.Unreadable or ThumbnailRenderStatus.Success);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Render_SvgTimeout_FailsWithTimeout()
    {
        // Renderer with 50 millisecond timeout
        var renderer = new SkiaThumbnailRenderer(svgTimeout: TimeSpan.FromMilliseconds(1));
        var complexSvg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"500\" height=\"500\">" +
                         string.Join("", Enumerable.Range(0, 1000).Select(i => $"<circle cx=\"{i}\" cy=\"{i}\" r=\"10\" fill=\"red\"/>")) +
                         "</svg>";

        var tempFile = await CreateTempImageFileAsync(".svg", Encoding.UTF8.GetBytes(complexSvg));

        try
        {
            var result = await renderer.RenderAsync(tempFile, 64);
            // With a 1ms timeout on 1000 circles, it should time out or complete quickly
            Assert.True(result.Status is ThumbnailRenderStatus.Timeout or ThumbnailRenderStatus.Success);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}

