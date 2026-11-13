using SkiaSharp;

namespace UsbHelper.App.Report;

/// <summary>
/// The page grid and the flow of blocks down A4 pages. Blocks are kept whole: one that does not fit starts a new page.
/// Drawing waits until every page is laid out, so the footer can say "Page X of Y".
/// </summary>
internal sealed class ReportLayout
{
    public const float PageWidth = 595.28f;
    public const float PageHeight = 841.89f;
    public static readonly float Top = Mm(20);
    public static readonly float Bottom = PageHeight - Mm(20);
    public static readonly float Left = Mm(18);
    public static readonly float Right = PageWidth - Mm(18);
    public static readonly float Width = Right - Left;

    // 12 columns with 12 pt gutters.
    private const float Gutter = 12;
    private static readonly float Column = (Width - 11 * Gutter) / 12;

    private readonly List<List<Action<SKCanvas>>> pages = [[]];

    public static float Mm(float mm) => mm / 25.4f * 72;

    /// <summary>The left edge of grid column <paramref name="column"/> (1 to 12).</summary>
    public static float X(int column) => Left + (column - 1) * (Column + Gutter);

    /// <summary>The width of <paramref name="columns"/> grid columns and the gutters between them.</summary>
    public static float Span(int columns) => columns * Column + (columns - 1) * Gutter;

    /// <summary>Where the next block starts on the current page.</summary>
    public float Y { get; private set; } = Top;

    public IReadOnlyList<IReadOnlyList<Action<SKCanvas>>> Pages => pages;

    /// <summary>Whether <paramref name="height"/> still fits on the current page.</summary>
    public bool Fits(float height) => Y + height <= Bottom;

    public void NewPage()
    {
        pages.Add([]);
        Y = Top;
    }

    /// <summary>Empty space; dropped at the top of a page.</summary>
    public void Space(float height)
    {
        if (Y > Top)
        {
            Y = Math.Min(Y + height, Bottom);
        }
    }

    /// <summary>
    /// Places a block of <paramref name="height"/>, drawn by <paramref name="draw"/> with its top y. A block that does
    /// not fit goes to a new page, after what <paramref name="continued"/> places there first.
    /// </summary>
    public void Place(float height, Action<SKCanvas, float> draw, Action? continued = null)
    {
        if (!Fits(height) && Y > Top)
        {
            NewPage();
            continued?.Invoke();
        }
        var top = Y;
        pages[^1].Add(canvas => draw(canvas, top));
        Y += height;
    }
}
