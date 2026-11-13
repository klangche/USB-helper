using SkiaSharp;
using Svg;
using Svg.Skia;
using UsbHelper.App.Resources;

namespace UsbHelper.App.Settings;

/// <summary>
/// The company logo (#228), stored in its own folder: the original as <c>logo-original.svg|png|webp</c> (an SVG stays
/// vector) and <c>logo-32.png</c> to <c>logo-512.png</c>, where the number is the height in pixels, the width follows the
/// aspect ratio and alpha is kept.
/// </summary>
public sealed class CompanyLogo(string folder)
{
    public static IReadOnlyList<int> Heights { get; } = [32, 64, 128, 256, 512];

    /// <summary>A source this many times wider than high, or higher than wide, is refused.</summary>
    public const int MaxAspectRatio = 8;

    public string Folder { get; } = folder;

    /// <summary>Moves a folder; tests replace it to make the swap fail.</summary>
    internal Action<string, string> MoveFolder { get; init; } = Directory.Move;

    private string Old => Folder + ".old";

    /// <summary>The imported file, or null when there is no logo.</summary>
    public string? Original
    {
        get
        {
            Restore();
            return Directory.Exists(Folder) ? Directory.EnumerateFiles(Folder, "logo-original.*").FirstOrDefault() : null;
        }
    }

    /// <summary>The PNG <paramref name="height"/> pixels high; it exists when <see cref="Original"/> does.</summary>
    public string Png(int height) => Path.Combine(Folder, $"logo-{height}.png");

    /// <summary>
    /// Replaces the logo with <paramref name="source"/>, an SVG, PNG or WebP of any size. The new set is written next to
    /// the folder first and then swapped in, so a refused or failed import leaves the previous logo as it was.
    /// </summary>
    /// <exception cref="InvalidDataException">Not an SVG, PNG or WebP, or one that cannot be read; the message says why.</exception>
    /// <exception cref="IOException">The files could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder is not writable.</exception>
    public void Import(string source)
    {
        var extension = Path.GetExtension(source).ToLowerInvariant();
        using var image = extension switch
        {
            ".svg" => Svg(source),
            ".png" => Raster(source, SKEncodedImageFormat.Png),
            ".webp" => Raster(source, SKEncodedImageFormat.Webp),
            _ => throw new InvalidDataException(Strings.LogoWrongType),
        };

        var temporary = Folder + ".new";
        try
        {
            Restore();
            DeleteFolder(temporary);
            Directory.CreateDirectory(temporary);
            File.Copy(source, Path.Combine(temporary, "logo-original" + extension));
            foreach (var height in Heights)
            {
                using var bitmap = Scaled(image, height);
                using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(Path.Combine(temporary, Path.GetFileName(Png(height))), png.ToArray());
            }
            DeleteFolder(Old);
            if (Directory.Exists(Folder))
            {
                MoveFolder(Folder, Old);
            }
            try
            {
                MoveFolder(temporary, Folder);
            }
            catch
            {
                Restore();
                throw;
            }
            DeleteFolder(Old);
        }
        finally
        {
            DeleteFolder(temporary);
        }
    }

    /// <summary>Deletes the logo, also one left aside by an interrupted swap; no logo is fine.</summary>
    public void Remove()
    {
        DeleteFolder(Folder);
        DeleteFolder(Old);
    }

    // A swap that stopped after the old logo was moved aside: put it back. If that fails, it is tried again next time.
    private void Restore()
    {
        if (Directory.Exists(Folder) || !Directory.Exists(Old))
        {
            return;
        }
        try
        {
            MoveFolder(Old, Folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left as it is: the logo reads as none until a later call manages the move.
        }
    }

    // Refused before anything is drawn, so an extreme shape never allocates a huge bitmap.
    private static void CheckAspectRatio(float width, float height)
    {
        if (width > height * MaxAspectRatio || height > width * MaxAspectRatio)
        {
            throw new InvalidDataException(Strings.LogoAspectRatio);
        }
    }

    private static void DeleteFolder(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static SKImage Raster(string source, SKEncodedImageFormat format)
    {
        using var codec = SKCodec.Create(source);
        if (codec == null || codec.EncodedFormat != format)
        {
            throw new InvalidDataException(Strings.LogoUnreadable);
        }
        CheckAspectRatio(codec.Info.Width, codec.Info.Height);
        using var bitmap = SKBitmap.Decode(codec, codec.Info.WithAlphaType(SKAlphaType.Premul))
            ?? throw new InvalidDataException(Strings.LogoUnreadable);
        return SKImage.FromBitmap(bitmap);
    }

    // Drawn at twice the largest height, so every size is a downscale. The size is width and height, or else the viewBox;
    // an SVG with neither has no size.
    private static SKImage Svg(string source)
    {
        SvgDocument document;
        try
        {
            document = SvgDocument.Open<SvgDocument>(source);
        }
        catch (Exception e) when (e is not IOException and not UnauthorizedAccessException)
        {
            throw new InvalidDataException(Strings.LogoUnreadable, e);
        }
        var sized = document.Width.Type != SvgUnitType.Percentage && document.Height.Type != SvgUnitType.Percentage;
        if (!sized && (document.ViewBox.Width <= 0 || document.ViewBox.Height <= 0))
        {
            throw new InvalidDataException(Strings.LogoSvgWithoutSize);
        }
        using var svg = new SKSvg();
        var picture = svg.FromSvgDocument(document) ?? throw new InvalidDataException(Strings.LogoUnreadable);
        var bounds = picture.CullRect;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new InvalidDataException(Strings.LogoSvgWithoutSize);
        }
        CheckAspectRatio(bounds.Width, bounds.Height);
        using var bitmap = Transparent(bounds.Width / bounds.Height, Heights[^1] * 2, out var canvas);
        using (canvas)
        {
            canvas.Scale(bitmap.Height / bounds.Height);
            canvas.Translate(-bounds.Left, -bounds.Top);
            canvas.DrawPicture(picture);
        }
        return SKImage.FromBitmap(bitmap);
    }

    // Mipmaps when the image shrinks, cubic sampling when it grows.
    private static SKBitmap Scaled(SKImage image, int height)
    {
        var bitmap = Transparent((float)image.Width / image.Height, height, out var canvas);
        var sampling = height < image.Height
            ? new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
            : new SKSamplingOptions(SKCubicResampler.Mitchell);
        using (canvas)
        {
            canvas.DrawImage(image, new SKRect(0, 0, bitmap.Width, bitmap.Height), sampling);
        }
        return bitmap;
    }

    // A transparent bitmap <paramref name="height"/> pixels high and <paramref name="aspect"/> times as wide.
    private static SKBitmap Transparent(float aspect, int height, out SKCanvas canvas)
    {
        var bitmap = new SKBitmap(new SKImageInfo(Math.Max(1, (int)Math.Round(aspect * height)), height, SKColorType.Rgba8888, SKAlphaType.Premul));
        canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        return bitmap;
    }
}
