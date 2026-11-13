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
}
