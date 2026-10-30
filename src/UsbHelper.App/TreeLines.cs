using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using UsbHelper.App.ViewModels;

namespace UsbHelper.App;

/// <summary>
/// The tree lines left of one tree item (tree style B, owner decision in #43): a column per ancestor whose grey trunk
/// continues past this row, then the rounded elbow to this item, then a node column with a dot and, when the item
/// has children, the start of its own trunk. Thin grey trunks; the elbow to a hub or device is in its status colour.
/// </summary>
public sealed class TreeLines : Control
{
    public const double ColumnWidth = 16;
    private const double Radius = 6;
    private const double DotRadius = 2.5;

    private static readonly IPen TrunkPen = new ImmutablePen(Color.Parse(StatusColors.Trunk).ToUInt32(), 1);

    public static readonly StyledProperty<IReadOnlyList<bool>?> GuidesProperty =
        AvaloniaProperty.Register<TreeLines, IReadOnlyList<bool>?>(nameof(Guides));

    public static readonly StyledProperty<bool> HasLineProperty = AvaloniaProperty.Register<TreeLines, bool>(nameof(HasLine));

    public static readonly StyledProperty<bool> IsLastProperty = AvaloniaProperty.Register<TreeLines, bool>(nameof(IsLast));

    public static readonly StyledProperty<bool> HasChildrenProperty = AvaloniaProperty.Register<TreeLines, bool>(nameof(HasChildren));

    /// <summary>Status lines are drawn a little thicker than the trunks.</summary>
    public static readonly StyledProperty<bool> HasStatusProperty = AvaloniaProperty.Register<TreeLines, bool>(nameof(HasStatus));

    public static readonly StyledProperty<IBrush?> ElbowBrushProperty = AvaloniaProperty.Register<TreeLines, IBrush?>(nameof(ElbowBrush));

    static TreeLines()
    {
        AffectsMeasure<TreeLines>(GuidesProperty, HasLineProperty);
        AffectsRender<TreeLines>(GuidesProperty, HasLineProperty, IsLastProperty, HasChildrenProperty, HasStatusProperty, ElbowBrushProperty);
    }

    public IReadOnlyList<bool>? Guides
    {
        get => GetValue(GuidesProperty);
        set => SetValue(GuidesProperty, value);
    }

    public bool HasLine
    {
        get => GetValue(HasLineProperty);
        set => SetValue(HasLineProperty, value);
    }

    public bool IsLast
    {
        get => GetValue(IsLastProperty);
        set => SetValue(IsLastProperty, value);
    }

    public bool HasChildren
    {
        get => GetValue(HasChildrenProperty);
        set => SetValue(HasChildrenProperty, value);
    }

    public bool HasStatus
    {
        get => GetValue(HasStatusProperty);
        set => SetValue(HasStatusProperty, value);
    }

    public IBrush? ElbowBrush
    {
        get => GetValue(ElbowBrushProperty);
        set => SetValue(ElbowBrushProperty, value);
    }

    // Guides, elbow (only with a line), node.
    private int Columns => (HasLine ? (Guides?.Count ?? 0) + 1 : 0) + 1;

    protected override Size MeasureOverride(Size availableSize) => new(Columns * ColumnWidth, 0);

    public override void Render(DrawingContext context)
    {
        var height = Bounds.Height;
        var middle = Math.Round(height / 2) + 0.5;
        var brush = ElbowBrush ?? TrunkPen.Brush;
        var statusPen = new Pen(brush, HasStatus ? 1.5 : 1);
        var column = 0;

        if (HasLine)
        {
            foreach (var trunk in Guides ?? [])
            {
                if (trunk)
                {
                    var x = Center(column);
                    context.DrawLine(TrunkPen, new Point(x, 0), new Point(x, height));
                }
                column++;
            }
            var elbow = Center(column);
            // The parent's trunk down to the curve, and on past this row unless this is the last child.
            context.DrawLine(TrunkPen, new Point(elbow, 0), new Point(elbow, IsLast ? middle - Radius : height));
            var curve = new StreamGeometry();
            using (var draw = curve.Open())
            {
                draw.BeginFigure(new Point(elbow, middle - Radius), false);
                draw.ArcTo(new Point(elbow + Radius, middle), new Size(Radius, Radius), 0, false, SweepDirection.CounterClockwise);
                draw.LineTo(new Point(Center(column + 1), middle));
                draw.EndFigure(false);
            }
            context.DrawGeometry(null, statusPen, curve);
            column++;
        }

        var node = Center(column);
        if (HasChildren)
        {
            context.DrawLine(TrunkPen, new Point(node, middle), new Point(node, height));
        }
        context.DrawEllipse(brush, null, new Point(node, middle), DotRadius, DotRadius);
    }

    // Half a pixel off the column centre keeps one-pixel lines sharp.
    private static double Center(int column) => (column * ColumnWidth) + (ColumnWidth / 2) + 0.5;
}
