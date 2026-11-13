using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using SkiaSharp;
using Svg;
using Svg.Skia;
using UsbHelper.App.Resources;

namespace UsbHelper.App.Settings;

/// <summary>
/// The company logo (#228, #241), stored in its own folder: the original as <c>logo-original.svg|png|webp</c>, the crop
/// in <c>crop.json</c>, and the cropped logo, always <see cref="LogoCrop.Ratio"/>:1, as <c>logo-32.png</c> to
/// <c>logo-512.png</c> (the number is the height in pixels; alpha is kept, frame outside the image is transparent). An
/// SVG original is also kept cropped as <c>logo.svg</c>, so it stays vector; its PNGs are drawn from that file.
/// </summary>
public sealed class CompanyLogo(string folder)
{
    public static IReadOnlyList<int> Heights { get; } = [32, 64, 128, 256, 512];

    /// <summary>A raster source with more pixels than this is refused before it is decoded, so memory stays bounded.</summary>
    public const long MaxPixels = 50_000_000;

    // The longest side of the image shown in the crop window.
    private const int PreviewSize = 2048;

    public string Folder { get; } = folder;

    /// <summary>Moves a folder; tests replace it to make the swap fail.</summary>
    internal Action<string, string> MoveFolder { get; init; } = Directory.Move;

    private string Old => Folder + ".old";

    private string CropFile => Path.Combine(Folder, "crop.json");

    /// <summary>
    /// The imported file, or null when there is no logo. A logo stored before the crop tool gets the whole image centred
    /// (<see cref="LogoCrop.Contain"/>) on the first read.
    /// </summary>
    public string? Original
    {
        get
        {
            Restore();
            var original = Directory.Exists(Folder) ? Directory.EnumerateFiles(Folder, "logo-original.*").FirstOrDefault() : null;
            if (original != null && !File.Exists(CropFile))
            {
                Upgrade(original);
            }
            return original;
        }
    }

    /// <summary>The saved crop, or null when there is no logo or crop.json cannot be read.</summary>
    public LogoCrop? Crop
    {
        get
        {
            try
            {
                return Original == null ? null : JsonSerializer.Deserialize<LogoCrop>(File.ReadAllText(CropFile));
            }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    /// <summary>The cropped SVG, or null when there is no logo or the original is not an SVG.</summary>
    public string? Svg
    {
        get
        {
            var path = Path.Combine(Folder, "logo.svg");
            return Original != null && File.Exists(path) ? path : null;
        }
    }

    /// <summary>The PNG <paramref name="height"/> pixels high and three times as wide; it exists when <see cref="Original"/> does.</summary>
    public string Png(int height) => Path.Combine(Folder, $"logo-{height}.png");

    /// <summary>
    /// <paramref name="source"/> at most 2048 pixels on its longest side as PNG, for the crop window, and its width per
    /// unit of height.
    /// </summary>
    /// <exception cref="InvalidDataException">See <see cref="Import"/>.</exception>
    public static (byte[] Png, double Aspect) Preview(string source)
    {
        using var drawing = Drawing.Open(source);
        var scale = Math.Min(PreviewSize / Math.Max(drawing.Aspect, 1), drawing.Image?.Height ?? PreviewSize);
        using var bitmap = Transparent((int)Math.Round(drawing.Aspect * scale), (int)Math.Round(scale), out var canvas);
        using (canvas)
        {
            drawing.DrawWhole(canvas, bitmap.Width, bitmap.Height);
        }
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return (png.ToArray(), drawing.Aspect);
    }

    /// <summary>
    /// Replaces the logo with <paramref name="source"/>, an SVG, PNG or WebP of any size up to <see cref="MaxPixels"/>,
    /// cropped as <paramref name="crop"/> says, or else the whole image centred. The new set is written next to the
    /// folder first and then swapped in, so a refused or failed import leaves the previous logo as it was.
    /// </summary>
    /// <exception cref="InvalidDataException">Not an SVG, PNG or WebP, or one that cannot be read; the message says why.</exception>
    /// <exception cref="IOException">The files could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder is not writable.</exception>
    public void Import(string source, LogoCrop? crop = null)
    {
        Restore();
        Write(source, crop);
    }

    /// <summary>Crops the logo again from its original; there must be one.</summary>
    /// <exception cref="IOException">The files could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder is not writable.</exception>
    public void Recrop(LogoCrop crop) => Write(Original ?? throw new InvalidOperationException("There is no logo."), crop);

    /// <summary>Deletes the logo, also one left aside by an interrupted swap; no logo is fine.</summary>
    public void Remove()
    {
        DeleteFolder(Folder);
        DeleteFolder(Old);
    }

    // A logo from before #241. If it cannot be written now, it reads as the old files until a later call manages.
    private void Upgrade(string original)
    {
        try
        {
            Write(original, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
        }
    }

    // The original, crop.json, the PNGs and for an SVG logo.svg, written to a new folder and swapped in.
    private void Write(string source, LogoCrop? crop)
    {
        using var drawing = Drawing.Open(source);
        crop ??= LogoCrop.Contain(drawing.Aspect, 1);

        var temporary = Folder + ".new";
        try
        {
            DeleteFolder(temporary);
            Directory.CreateDirectory(temporary);
            File.Copy(source, Path.Combine(temporary, "logo-original" + Path.GetExtension(source).ToLowerInvariant()));
            File.WriteAllText(Path.Combine(temporary, "crop.json"), JsonSerializer.Serialize(crop));
            var svg = drawing.CroppedSvg(crop);
            if (svg != null)
            {
                File.WriteAllText(Path.Combine(temporary, "logo.svg"), svg);
            }
            using var cropped = svg == null ? null : SvgPicture(XDocument.Parse(svg).Root!, out _);
            foreach (var height in Heights)
            {
                using var bitmap = Transparent(LogoCrop.FrameWidth(height), height, out var canvas);
                using (canvas)
                {
                    if (cropped != null)
                    {
                        canvas.Scale(height / cropped.Picture!.CullRect.Height);
                        canvas.DrawPicture(cropped.Picture);
                    }
                    else
                    {
                        drawing.DrawCropped(canvas, crop, height);
                    }
                }
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

    private static void DeleteFolder(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    // A transparent bitmap.
    private static SKBitmap Transparent(int width, int height, out SKCanvas canvas)
    {
        var bitmap = new SKBitmap(new SKImageInfo(Math.Max(1, width), Math.Max(1, height), SKColorType.Rgba8888, SKAlphaType.Premul));
        canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        return bitmap;
    }

    // The drawing of an SVG element. The size is width and height, or else the viewBox; an SVG with neither has no size.
    private static SKSvg SvgPicture(XElement root, out SKRect bounds)
    {
        SvgDocument document;
        try
        {
            document = SvgDocument.FromSvg<SvgDocument>(root.ToString(SaveOptions.DisableFormatting));
        }
        catch (Exception e)
        {
            throw new InvalidDataException(Strings.LogoUnreadable, e);
        }
        var sized = document.Width.Type != SvgUnitType.Percentage && document.Height.Type != SvgUnitType.Percentage;
        if (!sized && (document.ViewBox.Width <= 0 || document.ViewBox.Height <= 0))
        {
            throw new InvalidDataException(Strings.LogoSvgWithoutSize);
        }
        var svg = new SKSvg();
        var picture = svg.FromSvgDocument(document);
        bounds = picture?.CullRect ?? SKRect.Empty;
        if (picture == null || bounds.Width <= 0 || bounds.Height <= 0)
        {
            svg.Dispose();
            throw new InvalidDataException(picture == null ? Strings.LogoUnreadable : Strings.LogoSvgWithoutSize);
        }
        return svg;
    }

    // A source image: a decoded raster, or an SVG kept as its XML and its drawing.
    private sealed class Drawing : IDisposable
    {
        public SKImage? Image { get; private init; }

        private XElement? svg;
        private SKSvg? picture;
        private SKRect bounds;

        /// <summary>Width per unit of height.</summary>
        public double Aspect => Image != null ? (double)Image.Width / Image.Height : bounds.Width / bounds.Height;

        public static Drawing Open(string source) => Path.GetExtension(source).ToLowerInvariant() switch
        {
            ".svg" => OpenSvg(source),
            ".png" => OpenRaster(source, SKEncodedImageFormat.Png),
            ".webp" => OpenRaster(source, SKEncodedImageFormat.Webp),
            _ => throw new InvalidDataException(Strings.LogoWrongType),
        };

        private static Drawing OpenRaster(string source, SKEncodedImageFormat format)
        {
            using var codec = SKCodec.Create(source);
            if (codec == null || codec.EncodedFormat != format)
            {
                throw new InvalidDataException(Strings.LogoUnreadable);
            }
            if ((long)codec.Info.Width * codec.Info.Height > MaxPixels)
            {
                throw new InvalidDataException(Strings.LogoTooLarge);
            }
            using var bitmap = SKBitmap.Decode(codec, codec.Info.WithAlphaType(SKAlphaType.Premul))
                ?? throw new InvalidDataException(Strings.LogoUnreadable);
            return new Drawing { Image = SKImage.FromBitmap(bitmap) };
        }

        private static Drawing OpenSvg(string source)
        {
            XElement root;
            try
            {
                using var reader = XmlReader.Create(source, new XmlReaderSettings
                {
                    // Entities declared in the file are expanded (some editors write them); nothing is fetched.
                    DtdProcessing = DtdProcessing.Parse,
                    XmlResolver = null,
                    MaxCharactersFromEntities = 1_000_000,
                });
                root = XDocument.Load(reader).Root!;
            }
            catch (XmlException e)
            {
                throw new InvalidDataException(Strings.LogoUnreadable, e);
            }
            var drawing = new Drawing { svg = root };
            drawing.picture = SvgPicture(root, out drawing.bounds);
            return drawing;
        }

        /// <summary>The whole image filling <paramref name="width"/> x <paramref name="height"/>.</summary>
        public void DrawWhole(SKCanvas canvas, int width, int height)
        {
            if (Image != null)
            {
                canvas.DrawImage(Image, new SKRect(0, 0, width, height), Sampling(height));
                return;
            }
            canvas.Scale(height / bounds.Height);
            canvas.Translate(-bounds.Left, -bounds.Top);
            canvas.DrawPicture(picture!.Picture);
        }

        /// <summary>A raster image cropped into a frame <paramref name="height"/> high.</summary>
        public void DrawCropped(SKCanvas canvas, LogoCrop crop, int height)
        {
            var (x, y, width, imageHeight) = crop.Place(Aspect, height);
            canvas.DrawImage(Image!, SKRect.Create((float)x, (float)y, (float)width, (float)imageHeight), Sampling((int)imageHeight));
        }

        /// <summary>
        /// For an SVG: a 3:1 SVG that places the original drawing as <paramref name="crop"/> says, as a nested svg element
        /// with the original's own viewBox, so it stays vector. Null for a raster image.
        /// </summary>
        public string? CroppedSvg(LogoCrop crop)
        {
            if (svg == null)
            {
                return null;
            }
            XNamespace ns = "http://www.w3.org/2000/svg";
            var inner = new XElement(svg);
            var viewBox = inner.Attribute("viewBox")?.Value ?? Numbers(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
            var (x, y, width, height) = crop.Place(Aspect, 1);
            foreach (var name in new[] { "x", "y", "width", "height", "viewBox", "preserveAspectRatio" })
            {
                inner.SetAttributeValue(name, null);
            }
            inner.Add(new XAttribute("x", Numbers(x)), new XAttribute("y", Numbers(y)), new XAttribute("width", Numbers(width)),
                new XAttribute("height", Numbers(height)), new XAttribute("viewBox", viewBox), new XAttribute("preserveAspectRatio", "none"));
            var outer = new XElement(ns + "svg", new XAttribute("viewBox", $"0 0 {LogoCrop.Ratio} 1"), inner);
            return outer.ToString();
        }

        private static string Numbers(params double[] values) =>
            string.Join(' ', values.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));

        // Mipmaps when the image shrinks, cubic sampling when it grows.
        private SKSamplingOptions Sampling(int height) => height < Image!.Height
            ? new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
            : new SKSamplingOptions(SKCubicResampler.Mitchell);

        public void Dispose()
        {
            Image?.Dispose();
            picture?.Dispose();
        }
    }
}
