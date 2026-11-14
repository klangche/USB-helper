using System.Globalization;
using System.Xml;
using SkiaSharp;
using Svg;
using Svg.Skia;
using UsbHelper.App.Resources;
using UsbHelper.App.ViewModels;
using UsbHelper.Core.Diagnostics;

namespace UsbHelper.App.Report;

/// <summary>
/// The PDF sign-off report (#231): an A4 document an installer hands to the customer. All text is real text in the
/// embedded Inter font; the logo, rules, status marks and tree lines are vector. No view code, so it
/// runs headless.
/// </summary>
public static class ReportPdf
{
    internal static readonly SKColor Ink = SKColor.Parse("#1A1A1A");
    internal static readonly SKColor Muted = SKColor.Parse("#737373");
    internal static readonly SKColor Rule = SKColor.Parse("#D0D0D0");
    internal static readonly SKColor Accent = SKColor.Parse("#1F3A5F");

    internal static readonly TextStyle Title = new(ReportFace.SemiBold, 22, Ink, -0.2f);
    internal static readonly TextStyle Section = new(ReportFace.SemiBold, 11, Accent, 0.4f);
    internal static readonly TextStyle Body = new(ReportFace.Regular, 9.5f, Ink);
    internal static readonly TextStyle Strong = new(ReportFace.SemiBold, 9.5f, Ink);
    internal static readonly TextStyle Table = new(ReportFace.Regular, 8.5f, Ink);
    internal static readonly TextStyle TableHead = new(ReportFace.Medium, 7.5f, Muted, 0.3f);
    internal static readonly TextStyle Caption = new(ReportFace.Regular, 7.5f, Muted);
    internal static readonly TextStyle Figure = new(ReportFace.Regular, 16, Ink);
    internal static readonly TextStyle Result = new(ReportFace.SemiBold, 18, Ink, -0.1f);

    internal const float BodyLine = 9.5f * 1.35f;
    internal const float TableRow = 17;

    // One report at a time per process: every report shares the Inter typefaces and their HarfBuzz fonts
    // (ReportFace), and reports written at the same time on Windows got a corrupt glyph to text mapping (NUL text).
    private static readonly Lock Gate = new();

    /// <summary>Writes the report for <paramref name="model"/> to <paramref name="output"/> as a PDF; thread-safe.</summary>
    public static void Write(Stream output, ReportModel model)
    {
        lock (Gate)
        {
            WriteDocument(output, model);
        }
    }

    private static void WriteDocument(Stream output, ReportModel model)
    {
        var layout = new ReportLayout();
        // Drawn after the layout, so it lives until the document is written.
        using var logo = model.LogoPath == null ? null : Logo.Load(model.LogoPath);
        Header(layout, model, logo);
        Summary(layout, model);
        ReportTree.Place(layout, model.Tree);
        Results(layout, model);
        Displays(layout, model);
        Findings(layout, model);

        var computer = string.Join(" ", new[] { model.Computer.Manufacturer, model.Computer.Model }.OfType<string>());
        var metadata = SKDocumentPdfMetadata.Default;
        metadata.Title = string.Format(CultureInfo.InvariantCulture, Strings.ReportPdfTitle,
            model.RoomName ?? (computer.Length > 0 ? computer : Strings.UnknownValue));
        metadata.Author = model.CompanyName;
        metadata.Creator = string.Format(CultureInfo.InvariantCulture, Strings.ReportCreator, model.AppVersion);
        metadata.Creation = model.Created;
        metadata.Modified = model.Created;
        using var document = SKDocument.CreatePdf(output, metadata);
        var pages = layout.Pages;
        for (var i = 0; i < pages.Count; i++)
        {
            var canvas = document.BeginPage(ReportLayout.PageWidth, ReportLayout.PageHeight);
            foreach (var draw in pages[i])
            {
                draw(canvas);
            }
            Footer(canvas, model, logo != null, i + 1, pages.Count);
            document.EndPage();
        }
        document.Close();
    }

    internal static SKPaint Line(SKColor color, float width = 0.5f) =>
        new() { Color = color, StrokeWidth = width, IsStroke = true, IsAntialias = true };

    internal static void Hairline(SKCanvas canvas, float y, float left = 0, float right = 0)
    {
        using var paint = Line(Rule);
        canvas.DrawLine(left == 0 ? ReportLayout.Left : left, y, right == 0 ? ReportLayout.Right : right, y, paint);
    }

    /// <summary>A filled status dot centred on (x, y).</summary>
    internal static void Dot(SKCanvas canvas, float x, float y, float radius, SKColor color)
    {
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        canvas.DrawCircle(x, y, radius, paint);
    }

    internal static SKColor Color(ConnectionStatus status) => SKColor.Parse(StatusColors.Hex(status));

    internal static string Capitalized(string word) => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..];

    internal const float HeadingHeight = 22;

    // A section heading, kept with the first <paramref name="next"/> points of its content. Space separates the
    // sections, not a rule (#247).
    internal static void Heading(ReportLayout layout, string text, float next)
    {
        // The space first: it is dropped at the top of a page, and counts when the heading stays on this one.
        layout.Space(22);
        if (!layout.Fits(HeadingHeight + next))
        {
            layout.NewPage();
        }
        layout.Place(HeadingHeight, (canvas, top) => Section.Draw(canvas, text, ReportLayout.Left, top + 11));
    }

    // The logo, or the company name where there is none (#232): never both.
    private static void Header(ReportLayout layout, ReportModel model, Logo? logo)
    {
        const float captionLine = 7.5f * 1.4f;
        var align = logo == null ? Align.Left : Align.Right;
        var x = logo == null ? ReportLayout.Left : ReportLayout.Right;
        var lines = new List<(TextStyle Style, string Text, float Advance)>();
        var company = logo == null ? model.CompanyName : null;
        if (company != null)
        {
            lines.Add((Strong.With(Accent), company, 22));
        }
        lines.Add((Title, Strings.ReportTitle, 20));
        lines.Add(model.RoomName is { } room
            ? (new TextStyle(ReportFace.Regular, 11, Ink), room, 17)
            : (new TextStyle(ReportFace.Regular, 11, Muted, Oblique: true), Strings.ReportRoomMissing, 17));
        lines.Add((Caption, string.Format(CultureInfo.InvariantCulture, Strings.ReportCreatedVersion,
            model.Created.ToString("d MMMM yyyy, HH:mm", CultureInfo.InvariantCulture), model.AppVersion), captionLine));
        if (model.LicensedTo is { } email)
        {
            lines.Add((Caption, string.Format(CultureInfo.InvariantCulture, Strings.ReportLicensedTo, email), captionLine));
        }
        var textHeight = lines.Sum(line => line.Advance) - captionLine + 8;
        var height = Math.Max(textHeight, logo == null ? 0 : Logo.Box.Height) + 18;
        layout.Place(height, (canvas, top) =>
        {
            logo?.Draw(canvas, Logo.Box with { Location = new SKPoint(ReportLayout.Left, top) });
            var baseline = top + (company == null ? Title.Size * 0.8f : 9);
            foreach (var (style, text, advance) in lines)
            {
                style.Draw(canvas, text, x, baseline, align);
                baseline += advance;
            }
            Hairline(canvas, top + height - 1);
        });
    }

    private static void Summary(ReportLayout layout, ReportModel model)
    {
        Heading(layout, Strings.ReportSummary, 150);
        var computer = string.Join(" ", new[] { model.Computer.Manufacturer, model.Computer.Model }.OfType<string>());
        var profiles = model.Profiles.Select(profile => profile.Label).ToList();
        var left = ReportLayout.X(1);
        var right = ReportLayout.X(8);
        var height = Math.Max(30 + 34 + profiles.Count * BodyLine, 76);
        layout.Place(height, (canvas, top) =>
        {
            Caption.Draw(canvas, Strings.ReportComputer, left, top + 8);
            new TextStyle(ReportFace.Medium, 11, Ink).Draw(canvas, computer.Length > 0 ? computer : Strings.UnknownValue, left, top + 24);
            Caption.Draw(canvas, Strings.ReportCheckedProfiles, left, top + 46);
            var y = top + 60;
            foreach (var profile in model.Profiles)
            {
                Body.Draw(canvas, profile.Label, left, y);
                y += BodyLine;
            }

            Caption.Draw(canvas, Strings.ReportOverall, right, top + 8);
            var color = Color(model.Overall);
            Dot(canvas, right + 5, top + 26 - Result.CapHalf, 5, color);
            Result.Draw(canvas, Capitalized(StatusColors.Word(model.Overall)), right + 16, top + 26 + 2);
            // The explanation without the word repeated: "Potentially unstable: above a recommendation" gives "Above a recommendation".
            var explanation = StatusColors.Text(model.Overall);
            if (explanation.IndexOf(": ", StringComparison.Ordinal) is var colon and > 0)
            {
                var lineY = top + 46;
                foreach (var line in Caption.Wrap(Capitalized(explanation[(colon + 2)..]), ReportLayout.Right - right))
                {
                    Caption.Draw(canvas, line, right, lineY);
                    lineY += 7.5f * 1.4f;
                }
            }
        });

        var counts = model.Counts;
        string Number(int value) => counts.AtLeast
            ? string.Format(CultureInfo.InvariantCulture, Strings.ReportAtLeast, value)
            : value.ToString(CultureInfo.InvariantCulture);
        var figures = new (string Label, string Value, string? Note)[]
        {
            (Strings.LimitHubs, Number(counts.Hubs), counts.Usb2Hubs is { } usb2 && counts.Usb3Hubs is { } usb3
                ? string.Format(CultureInfo.InvariantCulture, Strings.ReportPerBus, usb2, usb3) : null),
            (Strings.LimitTiers, Number(counts.DeepestTier), null),
            (Strings.LimitHops, Number(counts.DeepestHop), null),
            (Strings.LimitDisplays, counts.ExternalDisplays?.ToString(CultureInfo.InvariantCulture) ?? Strings.WordUnknown, null),
        };
        layout.Space(4);
        layout.Place(60, (canvas, top) =>
        {
            for (var i = 0; i < figures.Length; i++)
            {
                var x = ReportLayout.X(1 + i * 3);
                Caption.Draw(canvas, figures[i].Label, x, top + 16);
                Figure.Draw(canvas, figures[i].Value, x, top + 38);
                if (figures[i].Note is { } note)
                {
                    Caption.Draw(canvas, note, x, top + 52);
                }
            }
        });
    }

    // A table's header row: each column's title, left or right aligned to its edge.
    internal static void TableHeader(SKCanvas canvas, float top, IEnumerable<(string Text, float X, Align Align)> columns)
    {
        foreach (var (text, x, align) in columns)
        {
            TableHead.Draw(canvas, text, x, top + 11, align);
        }
        Hairline(canvas, top + TableRow - 0.5f);
    }

    // A result word with its colour mark in front, as in the app.
    private static void StatusWord(SKCanvas canvas, ConnectionStatus status, float x, float top)
    {
        Dot(canvas, x + 2.5f, top + TableRow / 2, 2.5f, Color(status));
        Table.Draw(canvas, Capitalized(StatusColors.Word(status)), x + 10, top + TableRow / 2 + Table.CapHalf);
    }

    // Each checked profile with its result, then only what is not stable: the problems grouped over the profiles, and
    // the limits that are not in the database in a grey note.
    private static void Results(ReportLayout layout, ReportModel model)
    {
        const float lineHeight = 8.5f * 1.35f;
        var name = ReportLayout.X(1);
        var measured = ReportLayout.X(4) + ReportLayout.Span(1);
        var limit = ReportLayout.X(5) + ReportLayout.Span(1);
        var result = ReportLayout.X(7);
        var appliesTo = ReportLayout.X(10);
        var allStable = model.Profiles.All(profile => profile.Overall == ConnectionStatus.Green);
        // The profiles' table is kept whole, with the "Stable for all" line under it.
        Heading(layout, Strings.ReportResults, (model.Profiles.Count + 1) * TableRow + (allStable ? 10 + TableRow : 0));
        layout.Place(TableRow, (canvas, top) => TableHeader(canvas, top,
        [
            (Strings.ReportColumnProfile, name, Align.Left),
            (Strings.ReportColumnResult, result, Align.Left),
        ]));
        foreach (var profile in model.Profiles)
        {
            layout.Place(TableRow, (canvas, top) =>
            {
                Table.Draw(canvas, Table.Fit(profile.Label, ReportLayout.Span(6)), name, top + TableRow / 2 + Table.CapHalf);
                StatusWord(canvas, profile.Overall, result, top);
                Hairline(canvas, top + TableRow - 0.5f);
            });
        }

        var limitations = ReportResults.Limitations(model.Profiles);
        if (allStable)
        {
            layout.Space(10);
            layout.Place(TableRow, (canvas, top) =>
            {
                Dot(canvas, name + 3, top + TableRow / 2, 3, Color(ConnectionStatus.Green));
                Strong.Draw(canvas, Strings.ReportAllStable, name + 12, top + TableRow / 2 + Strong.CapHalf);
            });
        }
        else if (limitations.Count > 0)
        {
            const float title = 24;
            float Height(ReportLimitation row) => TableRow + (row.Profiles.Count - 1) * lineHeight;
            layout.Space(16);
            if (!layout.Fits(title + TableRow + Height(limitations[0])))
            {
                layout.NewPage();
            }
            layout.Place(title, (canvas, top) => Strong.Draw(canvas, Strings.ReportLimitations, name, top + 12));
            layout.Place(TableRow, (canvas, top) => TableHeader(canvas, top,
            [
                (Strings.ReportColumnCheck, name, Align.Left),
                (Strings.ReportColumnMeasured, measured, Align.Right),
                (Strings.ReportColumnLimit, limit, Align.Right),
                (Strings.ReportColumnResult, result, Align.Left),
                (Strings.ReportColumnAppliesTo, appliesTo, Align.Left),
            ]));
            foreach (var row in limitations)
            {
                var height = Height(row);
                layout.Place(height, (canvas, top) =>
                {
                    var baseline = top + TableRow / 2 + Table.CapHalf;
                    Table.Draw(canvas, Table.Fit(row.Check, ReportLayout.Span(3)), name, baseline);
                    Table.With(row.Measured == null ? Muted : Ink).Draw(canvas,
                        row.Measured?.ToString(CultureInfo.InvariantCulture) ?? Strings.WordUnknown, measured, baseline, Align.Right);
                    Table.With(row.Limit == null ? Muted : Ink).Draw(canvas,
                        row.Limit?.ToString(CultureInfo.InvariantCulture) ?? "\u2013", limit, baseline, Align.Right);
                    StatusWord(canvas, row.Status, result, top);
                    foreach (var profile in row.Profiles)
                    {
                        Table.Draw(canvas, Table.Fit(profile, ReportLayout.Span(3)), appliesTo, baseline);
                        baseline += lineHeight;
                    }
                    Hairline(canvas, top + height - 0.5f);
                });
            }
        }

        // Grey lines under the results: where to find the reasons, then what could not be judged.
        var notes = new List<string>();
        if (ReportResults.SeeFindings(model.Profiles))
        {
            notes.Add(Strings.ReportSeeFindings);
        }
        var unknowns = ReportResults.Unknowns(model.Profiles);
        foreach (var (inDatabase, format) in new[] { (false, Strings.ReportNotInDatabase), (true, Strings.ReportNotCounted) })
        {
            var items = unknowns.Where(unknown => unknown.InDatabase == inDatabase).Select(unknown =>
                string.Format(CultureInfo.InvariantCulture, Strings.ReportUnknownItem, unknown.Check, string.Join(", ", unknown.Profiles))).ToList();
            if (items.Count > 0)
            {
                notes.Add(string.Format(CultureInfo.InvariantCulture, format, string.Join(", ", items)));
            }
        }
        if (notes.Count > 0)
        {
            const float noteLine = 7.5f * 1.4f;
            var lines = notes.SelectMany(note => Caption.Wrap(note, ReportLayout.Width)).ToList();
            layout.Space(10);
            layout.Place(lines.Count * noteLine + 4, (canvas, top) =>
            {
                var y = top + 8;
                foreach (var line in lines)
                {
                    Caption.Draw(canvas, line, name, y);
                    y += noteLine;
                }
            });
        }
    }

    private static void Displays(ReportLayout layout, ReportModel model)
    {
        Heading(layout, Strings.ReportDisplays, 2 * TableRow);
        if (model.Displays is not { Count: > 0 } displays)
        {
            layout.Place(BodyLine, (canvas, top) => Body.With(Muted).Draw(canvas,
                model.Displays == null ? Strings.ReportDisplaysUnknown : Strings.ReportNoDisplays, ReportLayout.Left, top + 9));
            return;
        }
        var monitor = ReportLayout.X(1);
        var output = ReportLayout.X(5);
        var connected = ReportLayout.X(9);
        layout.Place(TableRow, (canvas, top) => TableHeader(canvas, top,
        [
            (Strings.ReportColumnMonitor, monitor, Align.Left),
            (Strings.ReportColumnOutput, output, Align.Left),
            (Strings.ReportColumnConnectedTo, connected, Align.Left),
        ]));
        foreach (var display in displays)
        {
            layout.Place(TableRow, (canvas, top) =>
            {
                var baseline = top + TableRow / 2 + Table.CapHalf;
                Table.Draw(canvas, Table.Fit(display.Monitor, ReportLayout.Span(4)), monitor, baseline);
                Table.Draw(canvas, Table.Fit(Capitalized(display.Output), ReportLayout.Span(4)), output, baseline);
                Table.With(display.ConnectedTo == null ? Muted : Ink).Draw(canvas,
                    Table.Fit(display.ConnectedTo ?? Strings.ReportNotLinked, ReportLayout.Span(4)), connected, baseline);
                Hairline(canvas, top + TableRow - 0.5f);
            });
        }
    }

    // The findings, one short line each (#247): a mark, the subject in medium weight, the message, and the profiles it
    // applies to in grey where it is not the subject itself. Small labels for the groups, no rules.
    private static void Findings(ReportLayout layout, ReportModel model)
    {
        const float label = 17;
        const float gap = 9;
        var groups = new[]
        {
            (FactKind.DetectedFact, Strings.ReportDetectedFacts, (SKColor?)Accent),
            (FactKind.VerifiedDatabaseFact, Strings.ReportDatabaseFacts, Color(ConnectionStatus.Green)),
            (FactKind.PredictedRisk, Strings.ReportRisks, Color(ConnectionStatus.Orange)),
            (FactKind.Unknown, Strings.ReportUnknowns, null),
        };
        var items = ReportFindings.Compact(model.Findings).Select(finding =>
        {
            var lines = FindingLines(finding);
            return (Finding: finding, Lines: lines, Height: lines.Count * FindingLine + 3);
        }).ToList();
        // A group's label with its first item.
        float Opening(FactKind kind) => label + items.Where(item => item.Finding.Kind == kind).Select(item => item.Height).DefaultIfEmpty(0).First();
        Heading(layout, Strings.ReportFindings, Opening(groups[0].Item1));
        var first = true;
        foreach (var (kind, title, mark) in groups)
        {
            var findings = items.Where(item => item.Finding.Kind == kind).ToList();
            if (!first)
            {
                layout.Space(gap);
            }
            first = false;
            if (!layout.Fits(Opening(kind)))
            {
                layout.NewPage();
            }
            layout.Place(label, (canvas, top) =>
            {
                var x = ReportLayout.Left + TableHead.Draw(canvas, title, ReportLayout.Left, top + 10) + 6;
                Caption.Draw(canvas, findings.Count > 0 ? findings.Count.ToString(CultureInfo.InvariantCulture) : Strings.ReportNone, x, top + 10);
            });
            foreach (var (_, lines, height) in findings)
            {
                layout.Place(height, (canvas, top) =>
                {
                    var middle = top + FindingLine / 2;
                    if (mark is { } color)
                    {
                        Dot(canvas, ReportLayout.Left + 2.5f, middle, 2.2f, color);
                    }
                    else
                    {
                        using var ring = Line(Muted, 0.7f);
                        canvas.DrawCircle(ReportLayout.Left + 2.5f, middle, 1.9f, ring);
                    }
                    var y = middle + FindingText.CapHalf;
                    foreach (var line in lines)
                    {
                        foreach (var (style, x, text) in line)
                        {
                            style.Draw(canvas, text, x, y);
                        }
                        y += FindingLine;
                    }
                });
            }
        }
    }

    private static readonly TextStyle FindingText = new(ReportFace.Regular, 9, Ink);
    private static readonly TextStyle FindingSubject = new(ReportFace.Medium, 9, Ink);
    private static readonly TextStyle FindingProfile = new(ReportFace.Regular, 8, Muted);
    private const float FindingLine = 9 * 1.4f;
    private static readonly float FindingIndent = ReportLayout.Left + 11;

    // A finding's lines, each a list of runs: the subject, the message and the profiles, broken at spaces to the text
    // width. A run continues on the next line where it does not fit; the profiles are kept together.
    private static List<List<(TextStyle Style, float X, string Text)>> FindingLines(ReportFinding finding)
    {
        var parts = new List<(TextStyle Style, string[] Words, float Gap)>
        {
            (FindingSubject, finding.Subject.Split(' ', StringSplitOptions.RemoveEmptyEntries), 0),
            (FindingText, finding.Message.Split(' ', StringSplitOptions.RemoveEmptyEntries), 5),
        };
        if (finding.Profile is { } profile && profile != finding.Subject)
        {
            parts.Add((FindingProfile, [profile], 8));
        }
        var lines = new List<List<(TextStyle Style, float X, string Text)>> { new() };
        var x = FindingIndent;
        foreach (var (style, words, gap) in parts)
        {
            var start = x + gap;
            var run = "";
            foreach (var word in words)
            {
                var candidate = run.Length == 0 ? word : run + " " + word;
                if (start + style.Measure(candidate) <= ReportLayout.Right || (run.Length == 0 && start == FindingIndent))
                {
                    run = candidate;
                    continue;
                }
                if (run.Length > 0)
                {
                    lines[^1].Add((style, start, run));
                }
                lines.Add([]);
                start = FindingIndent;
                run = word;
            }
            lines[^1].Add((style, start, run));
            x = start + style.Measure(run);
        }
        return lines;
    }

    // The company name only when there is no logo (#232); the licensed email under the middle line.
    private static void Footer(SKCanvas canvas, ReportModel model, bool hasLogo, int page, int pages)
    {
        var y = ReportLayout.PageHeight - ReportLayout.Mm(12);
        var left = string.Join(" - ", new[] { hasLogo ? null : model.CompanyName, model.RoomName }.OfType<string>());
        Caption.Draw(canvas, Caption.Fit(left, ReportLayout.Span(4)), ReportLayout.Left, y);
        Caption.Draw(canvas, string.Format(CultureInfo.InvariantCulture, Strings.ReportGeneratedBy, model.AppVersion),
            ReportLayout.PageWidth / 2, y, Align.Center);
        if (model.LicensedTo is { } email)
        {
            Caption.Draw(canvas, Caption.Fit(string.Format(CultureInfo.InvariantCulture, Strings.ReportLicensedTo, email), ReportLayout.Span(4)),
                ReportLayout.PageWidth / 2, y + 7.5f * 1.4f, Align.Center);
        }
        Caption.Draw(canvas, string.Format(CultureInfo.InvariantCulture, Strings.ReportPage, page, pages), ReportLayout.Right, y, Align.Right);
    }

    /// <summary>
    /// The company logo: an SVG as vector, a PNG or WebP as an image. Drawn as large as fits a 3:1 box of 54 x 18 mm,
    /// centred and never stretched; the app's cropped logos (#241) fill it exactly.
    /// </summary>
    private sealed class Logo : IDisposable
    {
        public static readonly SKRect Box = SKRect.Create(ReportLayout.Mm(54), ReportLayout.Mm(18));

        private readonly SKSvg? svg;
        private readonly SKImage? image;

        private Logo(SKSvg? svg, SKImage? image)
        {
            this.svg = svg;
            this.image = image;
        }

        /// <summary>The logo, or null when the file is missing, cannot be read or parsed, or has no size: the report then has no logo.</summary>
        public static Logo? Load(string path)
        {
            // Disposed unless the logo takes it, also when reading the SVG fails.
            SKSvg? svg = null;
            try
            {
                if (Path.GetExtension(path).Equals(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    // A zero width or height draws nothing, though Svg.Skia still gives it a 1 x 1 picture: no logo,
                    // so the header shows the company name instead (#232).
                    var document = SvgDocument.Open<SvgDocument>(path);
                    static bool Zero(SvgUnit unit) => unit.Type != SvgUnitType.Percentage && unit.Value <= 0;
                    svg = new SKSvg();
                    if (!Zero(document.Width) && !Zero(document.Height) && svg.FromSvgDocument(document) is { CullRect: { Width: > 0, Height: > 0 } })
                    {
                        var logo = new Logo(svg, null);
                        svg = null;
                        return logo;
                    }
                    return null;
                }
                var image = SKImage.FromEncodedData(path);
                return image == null ? null : new Logo(null, image);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or XmlException or ArgumentException or SvgException
                or FormatException or InvalidCastException or NotSupportedException)
            {
                return null;
            }
            finally
            {
                svg?.Dispose();
            }
        }

        public void Draw(SKCanvas canvas, SKRect box)
        {
            var size = svg?.Picture?.CullRect.Size ?? new SKSize(image!.Width, image.Height);
            var scale = Math.Min(box.Height / size.Height, box.Width / size.Width);
            var x = box.MidX - size.Width * scale / 2;
            var y = box.MidY - size.Height * scale / 2;
            var target = SKRect.Create(x, y, size.Width * scale, size.Height * scale);
            if (svg?.Picture is { } picture)
            {
                canvas.Save();
                canvas.Translate(x - picture.CullRect.Left * scale, y - picture.CullRect.Top * scale);
                canvas.Scale(scale);
                canvas.DrawPicture(picture);
                canvas.Restore();
            }
            else
            {
                canvas.DrawImage(image, target, new SKSamplingOptions(SKCubicResampler.Mitchell));
            }
        }

        public void Dispose()
        {
            svg?.Dispose();
            image?.Dispose();
        }
    }
}
