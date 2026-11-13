using System.Globalization;
using System.Xml;
using SkiaSharp;
using Svg.Skia;
using UsbHelper.App.Resources;
using UsbHelper.App.ViewModels;
using UsbHelper.Core.Diagnostics;

namespace UsbHelper.App.Report;

/// <summary>
/// The PDF sign-off report (#231): an A4 document an installer hands to the customer. All text is real text in the
/// embedded Inter font; the logo, rules, status marks, tree lines and signature lines are vector. No view code, so it
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

    /// <summary>Writes the report for <paramref name="model"/> to <paramref name="output"/> as a PDF.</summary>
    public static void Write(Stream output, ReportModel model)
    {
        var layout = new ReportLayout();
        // Drawn after the layout, so it lives until the document is written.
        using var logo = model.LogoPath == null ? null : Logo.Load(model.LogoPath);
        Header(layout, model, logo);
        Summary(layout, model);
        Checks(layout, model);
        Displays(layout, model);
        ReportTree.Place(layout, model.Tree);
        Findings(layout, model);
        SignOff(layout);

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
            Footer(canvas, model, i + 1, pages.Count);
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

    // A section heading with a hairline under it, kept with the first <paramref name="next"/> points of its content.
    internal static void Heading(ReportLayout layout, string text, float next)
    {
        const float height = 30;
        // The space first: it is dropped at the top of a page, and counts when the heading stays on this one.
        layout.Space(26);
        if (!layout.Fits(height + next))
        {
            layout.NewPage();
        }
        layout.Place(height, (canvas, top) =>
        {
            Section.Draw(canvas, text, ReportLayout.Left, top + 12);
            Hairline(canvas, top + 20);
        });
    }

    private static void Header(ReportLayout layout, ReportModel model, Logo? logo)
    {
        var align = logo == null ? Align.Left : Align.Right;
        var x = logo == null ? ReportLayout.Left : ReportLayout.Right;
        var lines = new List<(TextStyle Style, string Text, float Advance)>();
        if (model.CompanyName is { } company)
        {
            lines.Add((Strong.With(Accent), company, 22));
        }
        lines.Add((Title, Strings.ReportTitle, 20));
        lines.Add(model.RoomName is { } room
            ? (new TextStyle(ReportFace.Regular, 11, Ink), room, 17)
            : (new TextStyle(ReportFace.Regular, 11, Muted, Oblique: true), Strings.ReportRoomMissing, 17));
        lines.Add((Caption, string.Format(CultureInfo.InvariantCulture, Strings.ReportCreatedVersion,
            model.Created.ToString("d MMMM yyyy, HH:mm", CultureInfo.InvariantCulture), model.AppVersion), 0));
        var textHeight = lines.Sum(line => line.Advance) + 8;
        var height = Math.Max(textHeight, logo == null ? 0 : Logo.MaxHeight) + 18;
        layout.Place(height, (canvas, top) =>
        {
            logo?.Draw(canvas, ReportLayout.Left, top, ReportLayout.Span(5));
            var baseline = top + (model.CompanyName == null ? Title.Size * 0.8f : 9);
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
                Caption.Draw(canvas, Capitalized(explanation[(colon + 2)..]), right, top + 46);
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
            Hairline(canvas, top);
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

    private static void Checks(ReportLayout layout, ReportModel model)
    {
        const float profileBlock = 26 + 5 * TableRow;
        Heading(layout, Strings.ReportChecks, profileBlock);
        var check = ReportLayout.X(1);
        var limit = ReportLayout.X(6) + ReportLayout.Span(1);
        var measured = ReportLayout.X(8) + ReportLayout.Span(1);
        var result = ReportLayout.X(10);
        var first = true;
        foreach (var profile in model.Profiles)
        {
            if (!first)
            {
                layout.Space(18);
            }
            first = false;
            if (!layout.Fits(profileBlock))
            {
                layout.NewPage();
            }
            layout.Place(26, (canvas, top) =>
            {
                Strong.Draw(canvas, profile.Label, check, top + 12);
                var color = Color(profile.Overall);
                var word = Capitalized(StatusColors.Word(profile.Overall));
                var width = Strong.Draw(canvas, word, ReportLayout.Right, top + 12, Align.Right);
                Dot(canvas, ReportLayout.Right - width - 7, top + 12 - Strong.CapHalf, 3, color);
            });
            layout.Place(TableRow, (canvas, top) => TableHeader(canvas, top,
            [
                (Strings.ReportColumnCheck, check, Align.Left),
                (Strings.ReportColumnLimit, limit, Align.Right),
                (Strings.ReportColumnMeasured, measured, Align.Right),
                (Strings.ReportColumnResult, result, Align.Left),
            ]));
            foreach (var row in profile.Checks)
            {
                layout.Place(TableRow, (canvas, top) =>
                {
                    var baseline = top + TableRow / 2 + Table.CapHalf;
                    Table.Draw(canvas, row.Name, check, baseline);
                    Table.With(row.Limit == null ? Muted : Ink).Draw(canvas,
                        row.Limit?.ToString(CultureInfo.InvariantCulture) ?? Strings.WordUnknown, limit, baseline, Align.Right);
                    Table.With(row.Measured == null ? Muted : Ink).Draw(canvas,
                        row.Measured?.ToString(CultureInfo.InvariantCulture) ?? Strings.WordUnknown, measured, baseline, Align.Right);
                    var (word, status) = row.State switch
                    {
                        LimitState.Within => (Strings.LimitWithin, ConnectionStatus.Green),
                        LimitState.Exceeded => (Strings.LimitExceeded, ConnectionStatus.Purple),
                        _ => (Strings.LimitUnknown, ConnectionStatus.Grey),
                    };
                    Dot(canvas, result + 2.5f, top + TableRow / 2, 2.5f, Color(status));
                    Table.Draw(canvas, Capitalized(word), result + 10, baseline);
                    Hairline(canvas, top + TableRow - 0.5f);
                });
            }
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

    private static void Findings(ReportLayout layout, ReportModel model)
    {
        var subject = ReportLayout.X(1);
        var message = ReportLayout.X(5);
        var groups = new[]
        {
            (FactKind.DetectedFact, Strings.ReportDetectedFacts),
            (FactKind.VerifiedDatabaseFact, Strings.ReportDatabaseFacts),
            (FactKind.PredictedRisk, Strings.ReportRisks),
            (FactKind.Unknown, Strings.ReportUnknowns),
        };
        const float groupHeading = 22;
        const float none = BodyLine + 10;
        var items = model.Findings.Select(finding =>
        {
            var subjectLines = Strong.Wrap(finding.Subject, ReportLayout.Span(4));
            var messageLines = Body.Wrap(finding.Message, ReportLayout.Span(8));
            var lines = Math.Max(subjectLines.Count + (finding.Profile == null ? 0 : 1), messageLines.Count);
            return (Finding: finding, SubjectLines: subjectLines, MessageLines: messageLines, Height: lines * BodyLine + 11);
        }).ToList();
        // A group's heading with its first item, or with "None".
        float Opening(FactKind kind) => groupHeading + items.Where(item => item.Finding.Kind == kind).Select(item => item.Height).DefaultIfEmpty(none).First();
        Heading(layout, Strings.ReportFindings, Opening(groups[0].Item1));
        var first = true;
        foreach (var (kind, title) in groups)
        {
            var findings = items.Where(item => item.Finding.Kind == kind).ToList();
            if (!first)
            {
                layout.Space(12);
            }
            first = false;
            if (!layout.Fits(Opening(kind)))
            {
                layout.NewPage();
            }
            layout.Place(groupHeading, (canvas, top) =>
            {
                var width = Strong.Draw(canvas, title, subject, top + 12);
                if (findings.Count > 0)
                {
                    Caption.Draw(canvas, findings.Count.ToString(CultureInfo.InvariantCulture), subject + width + 6, top + 12);
                }
                Hairline(canvas, top + 21.5f);
            });
            if (findings.Count == 0)
            {
                layout.Place(none, (canvas, top) => Body.With(Muted).Draw(canvas, Strings.ReportNone, subject, top + 14));
                continue;
            }
            foreach (var (finding, subjectLines, messageLines, height) in findings)
            {
                layout.Place(height, (canvas, top) =>
                {
                    var y = top + 14;
                    foreach (var line in subjectLines)
                    {
                        Strong.Draw(canvas, line, subject, y);
                        y += BodyLine;
                    }
                    if (finding.Profile is { } profile)
                    {
                        Caption.Draw(canvas, Caption.Fit(profile, ReportLayout.Span(4)), subject, y - 1);
                    }
                    y = top + 14;
                    foreach (var line in messageLines)
                    {
                        Body.Draw(canvas, line, message, y);
                        y += BodyLine;
                    }
                    Hairline(canvas, top + height - 0.5f);
                });
            }
        }
    }

    private static void SignOff(ReportLayout layout)
    {
        // Column titles, the name, signature and date lines (the signature gets more room), then the ruled notes.
        float[] fields = [34, 46, 34];
        var notes = 22 + fields.Sum() + 3 * 12 + 34;
        var height = notes + 4 * 24 + 4;
        Heading(layout, Strings.ReportSignOff, height);
        layout.Place(height, (canvas, top) =>
        {
            using var pen = Line(SKColor.Parse("#9A9A9A"), 0.6f);
            foreach (var (title, column) in new[] { (Strings.ReportInstalledBy, 1), (Strings.ReportAcceptedBy, 7) })
            {
                var x = ReportLayout.X(column);
                var width = ReportLayout.Span(6);
                Strong.Draw(canvas, title, x, top + 12);
                var y = top + 22;
                string[] labels = [Strings.ReportName, Strings.ReportSignature, Strings.ReportDate];
                for (var i = 0; i < labels.Length; i++)
                {
                    y += fields[i];
                    canvas.DrawLine(x, y, x + width, y, pen);
                    Caption.Draw(canvas, labels[i], x, y + 10);
                    y += 12;
                }
            }
            Strong.Draw(canvas, Strings.ReportNotes, ReportLayout.Left, top + notes);
            for (var i = 1; i <= 4; i++)
            {
                Hairline(canvas, top + notes + i * 24);
            }
        });
    }

    private static void Footer(SKCanvas canvas, ReportModel model, int page, int pages)
    {
        var y = ReportLayout.PageHeight - ReportLayout.Mm(12);
        var left = string.Join(" - ", new[] { model.CompanyName, model.RoomName }.OfType<string>());
        Caption.Draw(canvas, Caption.Fit(left, ReportLayout.Span(4)), ReportLayout.Left, y);
        Caption.Draw(canvas, string.Format(CultureInfo.InvariantCulture, Strings.ReportGeneratedBy, model.AppVersion),
            ReportLayout.PageWidth / 2, y, Align.Center);
        Caption.Draw(canvas, string.Format(CultureInfo.InvariantCulture, Strings.ReportPage, page, pages), ReportLayout.Right, y, Align.Right);
    }

    /// <summary>The company logo: an SVG as vector, a PNG or WebP as an image. At most 18 mm high, aspect ratio kept.</summary>
    private sealed class Logo : IDisposable
    {
        public static readonly float MaxHeight = ReportLayout.Mm(18);

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
            try
            {
                if (Path.GetExtension(path).Equals(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    var svg = new SKSvg();
                    svg.Load(path);
                    if (svg.Picture is { CullRect: { Width: > 0, Height: > 0 } })
                    {
                        return new Logo(svg, null);
                    }
                    svg.Dispose();
                    return null;
                }
                var image = SKImage.FromEncodedData(path);
                return image == null ? null : new Logo(null, image);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or XmlException or ArgumentException)
            {
                return null;
            }
        }

        public void Draw(SKCanvas canvas, float x, float y, float maxWidth)
        {
            var size = svg?.Picture?.CullRect.Size ?? new SKSize(image!.Width, image.Height);
            var scale = Math.Min(MaxHeight / size.Height, maxWidth / size.Width);
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
