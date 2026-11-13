using SkiaSharp;
using UsbHelper.App.Resources;
using UsbHelper.App.ViewModels;

namespace UsbHelper.App.Report;

/// <summary>
/// The USB tree in the report: vector connector lines, a status mark and word per row, and the badges and notes
/// <see cref="TreeText"/> writes. Notes that do not fit after the name go on a second line of the same row. A row is never
/// split; after a page break a small "USB tree (continued)" heading comes first.
/// </summary>
internal static class ReportTree
{
    private const float Row = 14;
    private const float NoteLine = 10;
    private const float Step = 12;
    private const float Mark = 2.6f;
    private const float StatusWidth = 92;
    private static readonly SKColor Connector = SKColor.Parse(StatusColors.Trunk);
    private static readonly SKColor Badge = SKColor.Parse("#9A9A9A");
    private static readonly TextStyle BadgeText = new(ReportFace.Medium, 6.5f, ReportPdf.Muted, 0.2f);
    private static readonly TextStyle Note = ReportPdf.Table.With(ReportPdf.Muted);

    public static void Place(ReportLayout layout, IReadOnlyList<ReportRow> rows)
    {
        ReportPdf.Heading(layout, Strings.ReportTree, 3 * Row);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var hasChildren = i + 1 < rows.Count && rows[i + 1].Level > row.Level;
            var notes = string.Join(" · ", row.Notes);
            var inline = notes.Length == 0 || TextX(row) + Name(row).Measure(row.Text) + BadgesWidth(row) + 8 + Note.Measure(notes) <= NameEnd;
            layout.Place(inline ? Row : Row + NoteLine, (canvas, top) => Draw(canvas, row, notes, inline, hasChildren, top), () => layout.Place(24,
                (canvas, top) => ReportPdf.Strong.With(ReportPdf.Muted).Draw(canvas, Strings.ReportTreeContinued, ReportLayout.Left, top + 10)));
        }
    }

    // The centre of a row's mark at tree level <paramref name="level"/>.
    private static float MarkX(int level) => ReportLayout.Left + Mark + level * Step;

    private static float TextX(ReportRow row) => MarkX(row.Level) + 7;

    // Where the name, badges and notes end: the status word is right of it.
    private static float NameEnd => ReportLayout.Right - StatusWidth;

    // The host and the controllers stand out a little.
    private static TextStyle Name(ReportRow row) => row.Level <= 1 ? ReportPdf.Table with { Face = ReportFace.Medium } : ReportPdf.Table;

    private static IReadOnlyList<string> Badges(ReportRow row) => row.PortType is { } portType ? [.. row.Badges, portType] : row.Badges;

    private static float BadgesWidth(ReportRow row) => Badges(row).Sum(badge => BadgeText.Measure(badge) + 11);

    private static void Draw(SKCanvas canvas, ReportRow row, string notes, bool inline, bool hasChildren, float top)
    {
        var middle = top + Row / 2;
        var bottom = top + (inline ? Row : Row + NoteLine);
        using var line = ReportPdf.Line(Connector, 0.6f);
        for (var i = 0; i < row.Guides.Count; i++)
        {
            if (row.Guides[i])
            {
                canvas.DrawLine(MarkX(i), top, MarkX(i), bottom, line);
            }
        }
        var x = MarkX(row.Level);
        if (row.Level > 0)
        {
            var trunk = MarkX(row.Level - 1);
            canvas.DrawLine(trunk, top, trunk, row.IsLast ? middle : bottom, line);
            canvas.DrawLine(trunk, middle, x - Mark - 1, middle, line);
        }
        if (hasChildren)
        {
            canvas.DrawLine(x, middle + Mark + 1, x, bottom, line);
        }
        if (row.Status is { } status)
        {
            ReportPdf.Dot(canvas, x, middle, Mark, ReportPdf.Color(status));
        }
        else
        {
            using var ring = ReportPdf.Line(Connector, 0.8f);
            canvas.DrawCircle(x, middle, Mark - 0.4f, ring);
        }

        var baseline = middle + ReportPdf.Table.CapHalf;
        var textX = TextX(row);
        var name = Name(row).Fit(row.Text, NameEnd - textX - BadgesWidth(row));
        textX += Name(row).Draw(canvas, name, textX, baseline);
        foreach (var badge in Badges(row))
        {
            textX += 5;
            var width = BadgeText.Measure(badge) + 6;
            using var outline = ReportPdf.Line(Badge, 0.5f);
            canvas.DrawRoundRect(SKRect.Create(textX, middle - 4.5f, width, 9), 2, 2, outline);
            BadgeText.Draw(canvas, badge, textX + 3, middle + BadgeText.CapHalf);
            textX += width;
        }
        if (notes.Length > 0)
        {
            if (inline)
            {
                Note.Draw(canvas, notes, textX + 8, baseline);
            }
            else
            {
                Note.Draw(canvas, Note.Fit(notes, NameEnd - TextX(row)), TextX(row), baseline + NoteLine);
            }
        }
        if (row.Status is { } word)
        {
            ReportPdf.Table.Draw(canvas, ReportPdf.Capitalized(StatusColors.Word(word)), ReportLayout.Right, baseline, Align.Right);
        }
    }
}
