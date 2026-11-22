using SkiaSharp;

namespace UsbHelper.App.Settings;

/// <summary>The app's two themes, for the company logo (#340).</summary>
public enum LogoTheme
{
    Light,
    Dark,
}

/// <summary>
/// Whether the company logo can be seen on the top bar of a theme (#340): the WCAG contrast ratio between the logo's
/// visible pixels and the bar's background, at least <see cref="Minimum"/>. The logo is never put on another background.
/// </summary>
public static class LogoContrast
{
    /// <summary>The lowest contrast ratio at which a logo counts as visible (WCAG 2.1, graphical objects).</summary>
    public const double Minimum = 3;

    /// <summary>A pixel counts when its alpha is above this (0–255); fainter ones are anti-aliased edges or shadow.</summary>
    public const byte VisibleAlpha = 25;

    /// <summary>
    /// The relative luminance of the top bar's background: the Fluent theme's window background, white on light and black
    /// on dark (<c>SystemAltHighColor</c>).
    /// </summary>
    public static double Background(LogoTheme theme) => theme == LogoTheme.Light ? 1 : 0;

    /// <summary>The WCAG contrast ratio of two relative luminances, from 1 to 21.</summary>
    public static double Ratio(double first, double second) =>
        (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);

    /// <summary>
    /// The relative luminance of <paramref name="png"/>'s visible pixels, each weighted by its alpha; null when the file
    /// cannot be read or has no visible pixel.
    /// </summary>
    public static double? Luminance(string png)
    {
        try
        {
            using var bitmap = Decode(png);
            return bitmap == null ? null : Luminance(bitmap);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>See <see cref="Luminance(string)"/>.</summary>
    public static double? Luminance(SKBitmap bitmap)
    {
        double sum = 0, weight = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y); // Unpremultiplied.
                if (pixel.Alpha <= VisibleAlpha)
                {
                    continue;
                }
                sum += pixel.Alpha * (0.2126 * Linear(pixel.Red) + 0.7152 * Linear(pixel.Green) + 0.0722 * Linear(pixel.Blue));
                weight += pixel.Alpha;
            }
        }
        return weight == 0 ? null : sum / weight;
    }

    /// <summary>True when a logo of <paramref name="luminance"/> has a contrast of at least 3:1 on <paramref name="theme"/>.</summary>
    public static bool Visible(double? luminance, LogoTheme theme) =>
        luminance is { } value && Ratio(value, Background(theme)) >= Minimum;

    /// <summary><paramref name="source"/> with its colours inverted and its alpha kept, as PNG; null when it cannot be read.</summary>
    public static byte[]? Inverted(string source)
    {
        try
        {
            using var bitmap = Decode(source);
            if (bitmap == null)
            {
                return null;
            }
            using var inverted = new SKBitmap(new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            for (var y = 0; y < bitmap.Height; y++)
            {
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    inverted.SetPixel(x, y, new SKColor((byte)(255 - pixel.Red), (byte)(255 - pixel.Green), (byte)(255 - pixel.Blue), pixel.Alpha));
                }
            }
            using var data = inverted.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static SKBitmap? Decode(string path)
    {
        using var codec = SKCodec.Create(path);
        return codec == null ? null : SKBitmap.Decode(codec, codec.Info.WithAlphaType(SKAlphaType.Unpremul).WithColorType(SKColorType.Rgba8888));
    }

    // sRGB to linear light, as WCAG defines relative luminance.
    private static double Linear(byte channel)
    {
        var c = channel / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
