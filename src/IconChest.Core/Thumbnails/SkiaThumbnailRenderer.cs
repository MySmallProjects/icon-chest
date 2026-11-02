using System.Diagnostics;
using System.Xml;
using SkiaSharp;
using Svg.Skia;

namespace IconChest.Core.Thumbnails;

public sealed class SkiaThumbnailRenderer : IThumbnailRenderer
{
    private const long MaxMegapixels = 200_000_000;
    private static readonly TimeSpan DefaultSvgTimeout = TimeSpan.FromSeconds(10);
    private readonly TimeSpan _svgTimeout;

    public SkiaThumbnailRenderer(TimeSpan? svgTimeout = null)
    {
        _svgTimeout = svgTimeout ?? DefaultSvgTimeout;
    }

    public async Task<ThumbnailRenderResult> RenderAsync(
        string filePath,
        int targetSize,
        int quality = 85,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return ThumbnailRenderResult.Failed(ThumbnailRenderStatus.Unreadable, "File does not exist.");
        }

        if (targetSize <= 0)
        {
            targetSize = 64;
        }

        quality = Math.Clamp(quality, 1, 100);

        try
        {
            // Open read-only with FileShare.ReadWrite to avoid locking user files
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length == 0)
            {
                return ThumbnailRenderResult.Failed(ThumbnailRenderStatus.Unreadable, "File is zero bytes.");
            }

            // Sniff format
            var header = new byte[Math.Min(1024, stream.Length)];
            var readCount = await stream.ReadAsync(header.AsMemory(0, header.Length), cancellationToken);
            stream.Position = 0;

            if (IsSvg(header.AsSpan(0, readCount), filePath))
            {
                return await RenderSvgAsync(stream, targetSize, quality, cancellationToken);
            }

            return await Task.Run(() => RenderRaster(stream, targetSize, quality, cancellationToken), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return ThumbnailRenderResult.Failed(ThumbnailRenderStatus.Timeout, "Render operation was cancelled or timed out.");
        }
        catch (Exception ex)
        {
            return ThumbnailRenderResult.Failed(ThumbnailRenderStatus.Unreadable, ex.Message);
        }
    }

    private static bool IsSvg(ReadOnlySpan<byte> header, string filePath)
    {
        if (Path.GetExtension(filePath).Equals(".svg", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var text = System.Text.Encoding.UTF8.GetString(header);
        return text.Contains("<svg", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<ThumbnailRenderResult> RenderSvgAsync(
        Stream stream,
        int targetSize,
        int quality,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_svgTimeout);

        try
        {
            return await Task.Run(() =>
            {
                // Safe XML Reader configuration prohibiting external DTDs and entities
                var xmlSettings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    Async = false
                };

                using var reader = XmlReader.Create(stream, xmlSettings);
                var svg = new SKSvg();
                var picture = svg.Load(reader);

                if (picture == null || picture.CullRect.Width <= 0 || picture.CullRect.Height <= 0)
                {
                    return ThumbnailRenderResult.Failed(ThumbnailRenderStatus.Unreadable, "Invalid or empty SVG content.");
                }

                float svgWidth = picture.CullRect.Width;
                float svgHeight = picture.CullRect.Height;

                if ((long)svgWidth * (long)svgHeight > MaxMegapixels)
                {
                    return ThumbnailRenderResult.Failed(ThumbnailRenderStatus.TooLarge, "SVG dimensions exceed 200 megapixels.");
                }

                // SVG is the exception: vector is scaled to fit target size, so small SVGs are rendered sharp
                float scale = Math.Min((float)targetSize / svgWidth, (float)targetSize / svgHeight);
                int destWidth = Math.Max(1, (int)Math.Round(svgWidth * scale));
                int destHeight = Math.Max(1, (int)Math.Round(svgHeight * scale));

                var info = new SKImageInfo(destWidth, destHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
                using var surface = SKSurface.Create(info);
                if (surface == null)
                {
                    return ThumbnailRenderResult.Failed(ThumbnailRenderStatus.Unreadable, "Could not create Skia surface for SVG.");
                }

                var canvas = surface.Canvas;
                canvas.Clear(SKColors.Transparent);

                // Draw scaled vector picture
                canvas.Scale(scale);
                canvas.DrawPicture(picture);

                using var image = surface.Snapshot();
                using var data = image.Encode(SKEncodedImageFormat.Webp, quality);
                if (data == null || data.Size == 0)
                {
                    return ThumbnailRenderResult.Failed(ThumbnailRenderStatus.Unreadable, "Failed to encode WebP from SVG.");
                }

                return ThumbnailRenderResult.Ok(data.ToArray(), destWidth, destHeight);
            }, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return ThumbnailRenderResult.Failed(ThumbnailRenderStatus.Timeout, "SVG render timed out.");
        }
        catch (XmlException ex)
        {
            return ThumbnailRenderResult.Failed(ThumbnailRenderStatus.Unreadable, $"SVG XML parse error: {ex.Message}");
        }
        catch (Exception ex)
        {
            return ThumbnailRenderResult.Failed(ThumbnailRenderStatus.Unreadable, $"SVG rendering failed: {ex.Message}");
        }
    }

    private static ThumbnailRenderResult RenderRaster(
        Stream stream,
        int targetSize,
        int quality,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Check if stream is ICO and extract largest frame if applicable
        var effectiveStream = ExtractIcoLargestFrame(stream);

        using var codec = SKCodec.Create(effectiveStream);
        if (codec == null)
        {
            return ThumbnailRenderResult.Failed(ThumbnailRenderStatus.Unreadable, "Codec could not be created for image.");
        }

        // Bomb guard
        long pixelCount = (long)codec.Info.Width * codec.Info.Height;
        if (pixelCount > MaxMegapixels)
        {
            return ThumbnailRenderResult.Failed(ThumbnailRenderStatus.TooLarge, $"Image dimensions ({codec.Info.Width}x{codec.Info.Height}) exceed 200 megapixels.");
        }

        var origin = codec.EncodedOrigin;
        bool isSwapped = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        int naturalWidth = isSwapped ? codec.Info.Height : codec.Info.Width;
        int naturalHeight = isSwapped ? codec.Info.Width : codec.Info.Height;

        // Calculate output dimensions
        // NEVER upscale raster images: if image is smaller than target, keep original dimensions
        int destWidth;
        int destHeight;
        if (naturalWidth <= targetSize && naturalHeight <= targetSize)
        {
            destWidth = naturalWidth;
            destHeight = naturalHeight;
        }
        else
        {
            float scale = Math.Min((float)targetSize / naturalWidth, (float)targetSize / naturalHeight);
            destWidth = Math.Max(1, (int)Math.Round(naturalWidth * scale));
            destHeight = Math.Max(1, (int)Math.Round(naturalHeight * scale));
        }

        // Decode source bitmap
        var decodeInfo = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var sourceBitmap = new SKBitmap(decodeInfo);

        var codecOptions = new SKCodecOptions(0);
        var codecResult = codec.GetPixels(decodeInfo, sourceBitmap.GetPixels(), codecOptions);
        if (codecResult != SKCodecResult.Success && codecResult != SKCodecResult.IncompleteInput)
        {
            sourceBitmap.Dispose();
            return ThumbnailRenderResult.Failed(ThumbnailRenderStatus.Unreadable, $"Decoding failed with result: {codecResult}");
        }

        // Apply EXIF orientation
        var orientedBitmap = OrientBitmap(sourceBitmap, origin);

        try
        {
            // Render to destination surface with high-quality bicubic downscaling and premultiplied alpha
            var destInfo = new SKImageInfo(destWidth, destHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(destInfo);
            if (surface == null)
            {
                return ThumbnailRenderResult.Failed(ThumbnailRenderStatus.Unreadable, "Could not create Skia destination surface.");
            }

            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);

            using var paint = new SKPaint
            {
                IsAntialias = true
            };

            var sampling = new SKSamplingOptions(SKCubicResampler.Mitchell);
            var destRect = new SKRect(0, 0, destWidth, destHeight);
            canvas.DrawBitmap(orientedBitmap, destRect, sampling, paint);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Webp, quality);
            if (data == null || data.Size == 0)
            {
                return ThumbnailRenderResult.Failed(ThumbnailRenderStatus.Unreadable, "Failed to encode thumbnail to WebP.");
            }

            return ThumbnailRenderResult.Ok(data.ToArray(), destWidth, destHeight);
        }
        finally
        {
            orientedBitmap.Dispose();
        }
    }

    private static Stream ExtractIcoLargestFrame(Stream stream)
    {
        if (stream.Length < 6) return stream;

        long originalPos = stream.Position;
        try
        {
            using var reader = new BinaryReader(stream, System.Text.Encoding.Default, leaveOpen: true);
            var reserved = reader.ReadUInt16();
            var type = reader.ReadUInt16();
            var count = reader.ReadUInt16();

            if (reserved != 0 || type != 1 || count <= 1)
            {
                stream.Position = originalPos;
                return stream;
            }

            int maxPixels = 0;
            int bestOffset = 0;
            int bestLength = 0;

            for (int i = 0; i < count; i++)
            {
                int w = reader.ReadByte();
                int h = reader.ReadByte();
                if (w == 0) w = 256;
                if (h == 0) h = 256;
                reader.ReadByte(); // colors
                reader.ReadByte(); // reserved
                reader.ReadUInt16(); // planes
                reader.ReadUInt16(); // bit count
                int bytesInRes = reader.ReadInt32();
                int imageOffset = reader.ReadInt32();

                int pixels = w * h;
                if (pixels > maxPixels && imageOffset + bytesInRes <= stream.Length)
                {
                    maxPixels = pixels;
                    bestOffset = imageOffset;
                    bestLength = bytesInRes;
                }
            }

            if (bestLength > 0 && bestOffset > 0)
            {
                var buffer = new byte[bestLength];
                stream.Position = bestOffset;
                int read = stream.Read(buffer, 0, bestLength);
                if (read == bestLength)
                {
                    // Check if PNG frame
                    if (buffer.Length >= 4 && buffer[0] == 0x89 && buffer[1] == 0x50 && buffer[2] == 0x4E && buffer[3] == 0x47)
                    {
                        return new MemoryStream(buffer);
                    }

                    // Otherwise BMP DIB in ICO: construct BMP file header
                    using var ms = new MemoryStream();
                    using var bw = new BinaryWriter(ms);
                    bw.Write((byte)'B');
                    bw.Write((byte)'M');
                    bw.Write(14 + buffer.Length);
                    bw.Write((short)0);
                    bw.Write((short)0);
                    int dibHeaderSize = BitConverter.ToInt32(buffer, 0);
                    short bitCount = BitConverter.ToInt16(buffer, 14);
                    int colors = bitCount <= 8 ? (1 << bitCount) : 0;
                    int pixelOffset = 14 + dibHeaderSize + (colors * 4);
                    bw.Write(pixelOffset);
                    bw.Write(buffer);
                    return new MemoryStream(ms.ToArray());
                }
            }
        }
        catch
        {
            // Ignore parse errors and fall back to original stream
        }

        stream.Position = originalPos;
        return stream;
    }

    private static SKBitmap OrientBitmap(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft)
        {
            return source;
        }

        bool swapDimensions = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        int newWidth = swapDimensions ? source.Height : source.Width;
        int newHeight = swapDimensions ? source.Width : source.Height;

        var result = new SKBitmap(newWidth, newHeight, source.ColorType, source.AlphaType);
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);

        switch (origin)
        {
            case SKEncodedOrigin.TopRight:
                canvas.Scale(-1, 1, newWidth / 2.0f, newHeight / 2.0f);
                break;
            case SKEncodedOrigin.BottomRight:
                canvas.RotateDegrees(180, newWidth / 2.0f, newHeight / 2.0f);
                break;
            case SKEncodedOrigin.BottomLeft:
                canvas.Scale(1, -1, newWidth / 2.0f, newHeight / 2.0f);
                break;
            case SKEncodedOrigin.LeftTop:
                canvas.Translate(newWidth, 0);
                canvas.RotateDegrees(90);
                canvas.Scale(1, -1, source.Width / 2.0f, source.Height / 2.0f);
                break;
            case SKEncodedOrigin.RightTop:
                canvas.Translate(newWidth, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.RightBottom:
                canvas.Translate(newWidth, 0);
                canvas.RotateDegrees(90);
                canvas.Scale(-1, 1, source.Width / 2.0f, source.Height / 2.0f);
                break;
            case SKEncodedOrigin.LeftBottom:
                canvas.Translate(0, newHeight);
                canvas.RotateDegrees(270);
                break;
        }

        var sampling = new SKSamplingOptions(SKFilterMode.Nearest);
        canvas.DrawBitmap(source, 0, 0, sampling, null);
        source.Dispose();
        return result;
    }
}
