using SkiaSharp;

namespace UsbHelper.App.Tests.Settings;

/// <summary>Logo images made by the tests, so no binary is committed.</summary>
internal static class TestImages
{
    /// <summary>A 200 x 100 image in <paramref name="folder"/>: the left half transparent, the right half opaque red.</summary>
    public static string HalfRed(string folder, string name, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(200, 100, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            using var red = new SKPaint { Color = SKColors.Red };
            canvas.DrawRect(100, 0, 100, 100, red);
        }
        using var data = bitmap.Encode(format, 100);
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    /// <summary>A transparent PNG of <paramref name="width"/> x <paramref name="height"/> in <paramref name="folder"/>.</summary>
    public static string Blank(string folder, string name, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.Transparent);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    /// <summary>
    /// A PNG that says it is <paramref name="width"/> x <paramref name="height"/> but holds no pixels: enough to read its
    /// size, without making a huge image.
    /// </summary>
    public static byte[] PngHeader(int width, int height)
    {
        var size = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(size, width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(size.AsSpan(4), height);
        size[8] = 8; // 8 bits per channel,
        size[9] = 6; // RGBA.
        return [137, 80, 78, 71, 13, 10, 26, 10, .. Chunk("IHDR", size), .. Chunk("IDAT", []), .. Chunk("IEND", [])];
    }

    private static byte[] Chunk(string type, byte[] data)
    {
        byte[] typed = [.. System.Text.Encoding.ASCII.GetBytes(type), .. data];
        var chunk = new byte[typed.Length + 8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(chunk, data.Length);
        typed.CopyTo(chunk, 4);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(chunk.Length - 4), Crc32(typed));
        return chunk;
    }

    private static uint Crc32(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }
        return ~crc;
    }
}
