using System.Text;
using SkiaSharp;

namespace IconChest.Core.Tests.TestHelpers;

public static class ImageTestFixtures
{
    public static byte[] CreatePng(int width = 32, int height = 32, bool transparent = false)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(transparent ? new SKColor(255, 0, 0, 100) : SKColors.Red);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public static byte[] CreateJpeg(int width = 32, int height = 32)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Blue);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }

    public static byte[] CreateWebp(int width = 32, int height = 32, bool transparent = false)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(transparent ? new SKColor(0, 255, 0, 100) : SKColors.Green);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Webp, 90);
        return data.ToArray();
    }

    public static byte[] CreateBmp(int width = 2, int height = 2)
    {
        // 14-byte File Header + 40-byte DIB Header + pixels
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        var rowSize = (width * 3 + 3) & ~3;
        var pixelDataSize = rowSize * height;
        var fileSize = 54 + pixelDataSize;

        // File header
        bw.Write((byte)'B');
        bw.Write((byte)'M');
        bw.Write(fileSize);
        bw.Write((short)0); // reserved
        bw.Write((short)0);
        bw.Write(54); // pixel offset

        // DIB header (BITMAPINFOHEADER)
        bw.Write(40); // header size
        bw.Write(width);
        bw.Write(height);
        bw.Write((short)1); // planes
        bw.Write((short)24); // bpp
        bw.Write(0); // compression BI_RGB
        bw.Write(pixelDataSize);
        bw.Write(2835); // horizontal res (72 dpi)
        bw.Write(2835); // vertical res
        bw.Write(0); // colors
        bw.Write(0); // important colors

        // Pixels (BGR)
        var row = new byte[rowSize];
        for (var i = 0; i < width * 3; i += 3)
        {
            row[i] = 255; // Blue
            row[i + 1] = 0;
            row[i + 2] = 0;
        }

        for (var y = 0; y < height; y++)
        {
            bw.Write(row);
        }

        return ms.ToArray();
    }

    public static byte[] CreateGif(int width = 16, int height = 16)
    {
        // Minimal 1x1 or 16x16 valid GIF89a
        return new byte[]
        {
            0x47, 0x49, 0x46, 0x38, 0x39, 0x61, // GIF89a
            (byte)(width & 0xFF), (byte)((width >> 8) & 0xFF),
            (byte)(height & 0xFF), (byte)((height >> 8) & 0xFF),
            0xF0, 0x00, 0x00, // Global color table flag (2 colors)
            0x00, 0x00, 0x00, // Color 0: Black
            0xFF, 0xFF, 0xFF, // Color 1: White
            0x2C, 0x00, 0x00, 0x00, 0x00, // Image descriptor
            (byte)(width & 0xFF), (byte)((width >> 8) & 0xFF),
            (byte)(height & 0xFF), (byte)((height >> 8) & 0xFF),
            0x00,
            0x02, 0x02, 0x44, 0x01, 0x00, // LZW Raster data
            0x3B // Trailer
        };
    }

    public static byte[] CreateIco(int width = 16, int height = 16)
    {
        var pngData = CreatePng(width, height, transparent: true);
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // ICO Header
        bw.Write((short)0); // reserved
        bw.Write((short)1); // icon type
        bw.Write((short)1); // 1 image

        // Icon Directory Entry
        bw.Write((byte)(width >= 256 ? 0 : width));
        bw.Write((byte)(height >= 256 ? 0 : height));
        bw.Write((byte)0); // color count
        bw.Write((byte)0); // reserved
        bw.Write((short)1); // color planes
        bw.Write((short)32); // bpp
        bw.Write(pngData.Length); // size of data
        bw.Write(22); // offset (6 + 16 = 22)

        bw.Write(pngData);
        return ms.ToArray();
    }

    public static string CreateSvg(int width = 100, int height = 80, string unit = "px", string? viewBox = null)
    {
        var viewBoxAttr = viewBox != null ? $"viewBox=\"{viewBox}\"" : "";
        var widthAttr = width > 0 ? $"width=\"{width}{unit}\"" : "";
        var heightAttr = height > 0 ? $"height=\"{height}{unit}\"" : "";

        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" {widthAttr} {heightAttr} {viewBoxAttr}><rect width=\"100%\" height=\"100%\" fill=\"blue\"/></svg>";
    }

    public static byte[] CreateJpegWithExif(int width = 40, int height = 20, ushort orientation = 6, string dateTaken = "2026:10:30 14:30:00")
    {
        // Generate base JPEG
        var rawJpg = CreateJpeg(width, height);

        // Build an Exif APP1 segment:
        // Marker: FF E1
        // Length: (2 bytes)
        // Identifier: "Exif\0\0" (6 bytes)
        // TIFF Header: "II" (little-endian) + 0x002A + offset to IFD0 (8 bytes)
        using var exifStream = new MemoryStream();
        using var bw = new BinaryWriter(exifStream);

        // TIFF header at offset 0
        bw.Write((byte)'I');
        bw.Write((byte)'I');
        bw.Write((ushort)42);
        bw.Write((uint)8); // offset to IFD0

        // IFD0 starts at offset 8
        // Number of fields = 3: Orientation (0x0112), DateTimeOriginal in SubIFD or Date (0x0132), ExifSubIFD (0x8769)
        bw.Write((ushort)3);

        // Tag 1: Orientation (0x0112), SHORT (3), count 1, value orientation
        bw.Write((ushort)0x0112);
        bw.Write((ushort)3); // SHORT
        bw.Write((uint)1);   // count
        bw.Write((ushort)orientation);
        bw.Write((ushort)0); // padding

        // Tag 2: SubIFD offset (0x8769), LONG (4), count 1, value offset
        bw.Write((ushort)0x8769);
        bw.Write((ushort)4); // LONG
        bw.Write((uint)1);   // count
        bw.Write((uint)50);  // offset to SubIFD (after IFD0)

        // Tag 3: Make (0x010F), ASCII (2), count 6, offset 70
        bw.Write((ushort)0x010F);
        bw.Write((ushort)2);
        bw.Write((uint)6);
        bw.Write((uint)70);

        bw.Write((uint)0); // Next IFD offset = 0

        // SubIFD at offset 50:
        // Number of fields = 1: DateTimeOriginal (0x9003)
        bw.Write((ushort)1);
        bw.Write((ushort)0x9003); // DateTimeOriginal
        bw.Write((ushort)2);      // ASCII
        bw.Write((uint)20);       // "YYYY:MM:DD HH:MM:SS\0"
        bw.Write((uint)80);       // offset 80
        bw.Write((uint)0);

        // Offset 70: Make string "Canon\0"
        exifStream.Position = 70;
        bw.Write(Encoding.ASCII.GetBytes("Canon\0"));

        // Offset 80: Date string
        exifStream.Position = 80;
        bw.Write(Encoding.ASCII.GetBytes(dateTaken + "\0"));

        var tiffPayload = exifStream.ToArray();

        // Stitch into JPEG right after SOI (FF D8)
        using var finalJpg = new MemoryStream();
        finalJpg.Write(rawJpg, 0, 2); // SOI

        // APP1 marker
        finalJpg.WriteByte(0xFF);
        finalJpg.WriteByte(0xE1);

        var app1Len = (ushort)(2 + 6 + tiffPayload.Length);
        finalJpg.WriteByte((byte)(app1Len >> 8));
        finalJpg.WriteByte((byte)(app1Len & 0xFF));

        finalJpg.Write(Encoding.ASCII.GetBytes("Exif\0\0"));
        finalJpg.Write(tiffPayload);

        // Write remainder of raw JPEG (from offset 2)
        finalJpg.Write(rawJpg, 2, rawJpg.Length - 2);

        return finalJpg.ToArray();
    }
}

