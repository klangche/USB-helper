using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Platform;
using HarfBuzzSharp;
using SkiaSharp;

namespace UsbHelper.App.Report;

/// <summary>
/// One weight of Inter, the font Avalonia.Fonts.Inter ships, read from that assembly's resources. Text is shaped with
/// HarfBuzz (kerning, tabular figures) and drawn as glyph runs that carry their text, so the PDF embeds the font and
/// every word can be selected and copied.
/// </summary>
internal sealed class ReportFace
{
    public static readonly ReportFace Regular = new("Inter-Regular.ttf");
    public static readonly ReportFace Medium = new("Inter-Medium.ttf");
    public static readonly ReportFace SemiBold = new("Inter-SemiBold.ttf");

    // Kerning stays on. Contextual alternates and ligatures are off, so every glyph is the plain one for its character:
    // alternate glyphs (the font's own tabular figures among them) do not map back to text in every PDF reader.
    private static readonly Feature[] Features = [Feature.Parse("-calt"), Feature.Parse("-liga")];

    private readonly HarfBuzzSharp.Font shaper;
    private readonly int unitsPerEm;
    // Tabular figures for a text that is only a number: every digit gets the widest digit's advance, centred in it, so
    // numbers line up in columns. Digits in other text keep their own widths, so they read and copy as one word.
    private readonly int digitWidth;

    private ReportFace(string file)
    {
        var bytes = Read(file);
        using var fontData = SKData.CreateCopy(bytes);
        Typeface = SKTypeface.FromData(fontData);
        // Native memory, not Blob.FromStream: that one points HarfBuzz at a managed array the garbage collector can move.
        // Kept for the process's lifetime with the shaper, which reads the font data from it.
        var data = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, data, bytes.Length);
        using var blob = new Blob(data, bytes.Length, MemoryMode.ReadOnly);
        using var face = new Face(blob, 0);
        unitsPerEm = face.UnitsPerEm;
        shaper = new HarfBuzzSharp.Font(face);
        shaper.SetScale(unitsPerEm, unitsPerEm);
        digitWidth = Enumerable.Range('0', 10).Max(digit => shaper.TryGetGlyph((uint)digit, out var glyph) ? shaper.GetHorizontalGlyphAdvance(glyph) : 0);
    }

    public SKTypeface Typeface { get; }

    private static byte[] Read(string file)
    {
        // The standard loader, not AssetLoader: it needs no running Avalonia app, so the report also renders headless.
        using var stream = new StandardAssetLoader().Open(new Uri($"avares://Avalonia.Fonts.Inter/Assets/{file}"));
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>The glyphs of <paramref name="text"/>, their x positions and the total advance, at <paramref name="size"/> points.</summary>
    public Shaped Shape(string text, float size, float tracking)
    {
        var utf8 = Encoding.UTF8.GetBytes(text);
        using var buffer = new HarfBuzzSharp.Buffer();
        buffer.AddUtf8(utf8);
        buffer.GuessSegmentProperties();
        shaper.Shape(buffer, Features);
        var infos = buffer.GlyphInfos;
        var positions = buffer.GlyphPositions;
        var scale = size / unitsPerEm;
        var glyphs = new ushort[infos.Length];
        var xs = new float[infos.Length];
        var clusters = new uint[infos.Length];
        var tabular = text.Length > 0 && text.All(char.IsAsciiDigit);
        var x = 0f;
        for (var i = 0; i < infos.Length; i++)
        {
            glyphs[i] = (ushort)infos[i].Codepoint;
            clusters[i] = infos[i].Cluster;
            var advance = positions[i].XAdvance;
            var offset = positions[i].XOffset;
            if (tabular)
            {
                offset += (digitWidth - advance) / 2;
                advance = digitWidth;
            }
            xs[i] = x + offset * scale;
            x += advance * scale + tracking;
        }
        return new Shaped(utf8, glyphs, xs, clusters, infos.Length == 0 ? 0 : x - tracking);
    }
}

internal sealed record Shaped(byte[] Utf8, ushort[] Glyphs, float[] Positions, uint[] Clusters, float Width);

internal enum Align
{
    Left,
    Right,
    Center,
}

/// <summary>A text style: face, size in points, colour and letter spacing in points.</summary>
internal sealed record TextStyle(ReportFace Face, float Size, SKColor Color, float Tracking = 0, bool Oblique = false)
{
    // Inter's cap height, as a share of the size: puts text in the optical middle of a row.
    private const float CapHeight = 0.727f;

    public float CapHalf => Size * CapHeight / 2;

    public TextStyle With(SKColor color) => this with { Color = color };

    public float Measure(string text) => Face.Shape(text, Size, Tracking).Width;

    /// <summary>Draws <paramref name="text"/> on the baseline <paramref name="y"/>, aligned to <paramref name="x"/>; returns its width.</summary>
    public float Draw(SKCanvas canvas, string text, float x, float y, Align align = Align.Left)
    {
        var shaped = Face.Shape(text, Size, Tracking);
        if (shaped.Glyphs.Length == 0)
        {
            return 0;
        }
        var left = align switch
        {
            Align.Right => x - shaped.Width,
            Align.Center => x - shaped.Width / 2,
            _ => x,
        };
        using var font = new SKFont(Face.Typeface, Size) { Subpixel = true, SkewX = Oblique ? -0.18f : 0 };
        using var builder = new SKTextBlobBuilder();
        var run = builder.AllocateHorizontalTextRun(font, shaped.Glyphs.Length, 0, shaped.Utf8.Length);
        run.SetGlyphs(shaped.Glyphs);
        run.SetPositions(shaped.Positions);
        run.SetText(shaped.Utf8);
        run.SetClusters(shaped.Clusters);
        using var blob = builder.Build();
        using var paint = new SKPaint { Color = Color, IsAntialias = true };
        canvas.DrawText(blob, left, y, paint);
        return shaped.Width;
    }

    /// <summary>The text cut with an ellipsis so it fits <paramref name="width"/>; unchanged when it fits.</summary>
    public string Fit(string text, float width)
    {
        if (Measure(text) <= width)
        {
            return text;
        }
        var length = text.Length;
        while (length > 1 && Measure(text[..length].TrimEnd() + "…") > width)
        {
            length--;
        }
        return text[..length].TrimEnd() + "…";
    }

    /// <summary>The text broken into lines no wider than <paramref name="width"/>, at spaces.</summary>
    public IReadOnlyList<string> Wrap(string text, float width)
    {
        var lines = new List<string>();
        var line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && Measure(candidate) > width)
            {
                lines.Add(line);
                line = word;
            }
            else
            {
                line = candidate;
            }
        }
        lines.Add(line);
        return lines;
    }
}
