using System.Collections.ObjectModel;
using System.Globalization;
using UsbHelper.App.Resources;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;
using UsbHelper.Core.Topology;

namespace UsbHelper.App.ViewModels;

/// <summary>
/// One item in the tree view. <see cref="UpdateHost"/> changes the tree in place and keeps child items whose id is unchanged,
/// so selection survives updates and only changed items notify the view.
/// The top item is the host, this computer; the controllers are its children.
/// Ports with nothing on them are left out. Every item but the host has an elbow line from its parent; the view
/// draws one column per <see cref="Guides"/> entry before it.
/// Every trunk segment, from the host down, is drawn in the worst status of everything it leads to (#89); the elbow to a
/// hub or device keeps that item's own status.
/// The line from a port to its hub or device can be marked by the user as an active cable or extender the operating
/// system does not show (#76): its extra hops count for everything behind that port.
/// A hub built into the computer starts collapsed, everything else open (#68); the item keeps the user's choice
/// across updates. A collapsed item says how many items it hides (#91).
/// A DisplayLink device is shown as a display, and every hub above it says it has one (#92).
/// </summary>
public sealed class NodeViewModel : ObservableObject
{
    private string text = "";
    private ConnectionStatus? status;
    private IReadOnlyList<string?> guides = [];
    private bool isLast;
    private ConnectionStatus? pathStatus;
    private string trunkAboveColor = StatusColors.Trunk;
    private string? trunkBelowColor;
    private string trunkColor = StatusColors.Trunk;
    private string? counts;
    private bool isExpanded;
    private int itemsBelow;
    private bool isDisplay;
    private bool displayBelow;
    private bool isHub;
    private int markedHops;
    private readonly Action<string, int> mark;

    private NodeViewModel(string id, string? portId, bool isDisconnected, bool hasLine, bool isExpanded, Action<string, int> mark)
    {
        Id = id;
        PortId = portId;
        IsDisconnected = isDisconnected;
        this.mark = mark;
        HasLine = hasLine;
        this.isExpanded = isExpanded;
        Children.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasChildren));
            OnPropertyChanged(nameof(CanExpand));
            OnPropertyChanged(nameof(ShowsChildren));
        };
    }

    /// <summary>The id of the host item. Never a USB id: those are the operating system's device paths.</summary>
    public const string HostId = "host";

    public string Id { get; }

    /// <summary>The choices for a mark, by number of extra hops: none, then +1 to +5.</summary>
    public static IReadOnlyList<string> MarkChoices { get; } =
        [Strings.MarkNone, .. Enumerable.Range(1, 5).Select(hops => string.Format(CultureInfo.CurrentCulture, Strings.MarkHops, hops))];

    /// <summary>For a connected hub or device: the port its line comes from, where a mark is kept. Null otherwise.</summary>
    public string? PortId { get; }

    /// <summary>True when the line to this item can be marked as an active cable or extender.</summary>
    public bool CanMark => PortId != null;

    /// <summary>
    /// The user's mark on the line to this item: extra hops for an active cable or extender, 0 for none. Setting it
    /// re-evaluates everything; it is ignored when the line cannot be marked.
    /// </summary>
    public int ExtraHops
    {
        get => markedHops;
        set
        {
            if (PortId != null && value >= 0 && value != markedHops)
            {
                mark(PortId, value);
            }
        }
    }

    /// <summary>The user marked this line; the view draws it dashed with <see cref="MarkBadge"/>.</summary>
    public bool IsUserMarked => ExtraHops > 0;

    /// <summary>"+N hops" on a marked line, null otherwise.</summary>
    public string? MarkBadge => IsUserMarked ? string.Format(CultureInfo.CurrentCulture, Strings.CableMark, ExtraHops) : null;

    /// <summary>A device seen earlier, shown at its last port.</summary>
    public bool IsDisconnected { get; }

    public ObservableCollection<NodeViewModel> Children { get; } = [];

    public bool HasChildren => Children.Count > 0;

    /// <summary>Whether the children are shown. The user expands and collapses with a double-click or the arrow keys.</summary>
    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (SetField(ref isExpanded, value))
            {
                OnPropertyChanged(nameof(ShowsChildren));
                OnPropertyChanged(nameof(HiddenCount));
                OnPropertyChanged(nameof(CollapsedHint));
            }
        }
    }

    /// <summary>An item with children: the view shows its expand/collapse control.</summary>
    public bool CanExpand => HasChildren;

    /// <summary>How many items a collapsed item hides: everything below it. 0 when it is expanded.</summary>
    public int HiddenCount => IsExpanded ? 0 : itemsBelow;

    /// <summary>The children are visible, so the item's own trunk is drawn below it.</summary>
    public bool ShowsChildren => HasChildren && IsExpanded;

    /// <summary>After the name of a collapsed item: how many items it hides. Null otherwise.</summary>
    public string? CollapsedHint => HiddenCount > 0 ? string.Format(CultureInfo.CurrentCulture, Strings.CollapsedHint, HiddenCount) : null;

    /// <summary>A connected DisplayLink device: the view shows it as a display.</summary>
    public bool IsDisplay
    {
        get => isDisplay;
        private set
        {
            if (SetField(ref isDisplay, value))
            {
                OnPropertyChanged(nameof(DisplayNote));
            }
        }
    }

    /// <summary>
    /// After the name: "Extended display: DisplayLink driver required" on a DisplayLink device, "has a display" on a hub
    /// with one below it. Null otherwise.
    /// </summary>
    public string? DisplayNote => IsDisplay ? Strings.DisplayLinkDevice : isHub && displayBelow ? Strings.HubHasDisplay : null;

    public string Text
    {
        get => text;
        private set => SetField(ref text, value);
    }

    /// <summary>Status of the line to this hub or device; null for controllers, root hubs and ports.</summary>
    public ConnectionStatus? Status
    {
        get => status;
        private set
        {
            if (SetField(ref status, value))
            {
                OnPropertyChanged(nameof(LineColor));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusWord));
                OnPropertyChanged(nameof(HasStatus));
                OnPropertyChanged(nameof(ElbowColor));
            }
        }
    }

    /// <summary>The worst status of this item and everything below it; null when none of them has a status.</summary>
    public ConnectionStatus? PathStatus
    {
        get => pathStatus;
        private set
        {
            if (SetField(ref pathStatus, value))
            {
                OnPropertyChanged(nameof(ElbowColor));
            }
        }
    }

    public bool HasStatus => Status != null;

    /// <summary>The result word on the right, in <see cref="LineColor"/>; null for controllers, root hubs and ports.</summary>
    public string? StatusWord => Status is { } value ? StatusColors.Word(value) : null;

    /// <summary>
    /// The elbow from the parent: the item's own status colour for a hub or device; for a controller, root hub or port
    /// the worst status below it.
    /// </summary>
    public string ElbowColor => Color(Status ?? PathStatus);

    /// <summary>The parent's trunk down to this item's elbow: the worst status of this item and its later siblings.</summary>
    public string TrunkAboveColor
    {
        get => trunkAboveColor;
        private set => SetField(ref trunkAboveColor, value);
    }

    /// <summary>The parent's trunk on past this item's elbow, to its later siblings; null for the last child.</summary>
    public string? TrunkBelowColor
    {
        get => trunkBelowColor;
        private set => SetField(ref trunkBelowColor, value);
    }

    /// <summary>This item's own trunk, from its dot down to its children: the worst status of all of them.</summary>
    public string TrunkColor
    {
        get => trunkColor;
        private set => SetField(ref trunkColor, value);
    }

    /// <summary>False for the host, the top of the tree.</summary>
    public bool HasLine { get; }

    /// <summary>
    /// One entry per ancestor column left of the elbow: the colour of that ancestor's trunk where it continues past this
    /// row, null where it does not.
    /// </summary>
    public IReadOnlyList<string?> Guides
    {
        get => guides;
        private set
        {
            if (!guides.SequenceEqual(value))
            {
                guides = value;
                OnPropertyChanged(nameof(Guides));
            }
        }
    }

    /// <summary>The last child of its parent: its elbow ends the parent's trunk.</summary>
    public bool IsLast
    {
        get => isLast;
        private set => SetField(ref isLast, value);
    }

    /// <summary>For a controller: its hub count, deepest tier and deepest hop. Null for everything else.</summary>
    public string? Counts
    {
        get => counts;
        private set => SetField(ref counts, value);
    }

    public string LineColor => Status is { } value ? StatusColors.Hex(value) : StatusColors.None;

    public string? StatusText => Status is { } value ? StatusColors.Text(value) : null;

    /// <summary>The host item, the top of the tree without a line, with <paramref name="controllers"/> as its children.</summary>
    /// <param name="extraHops">The user's marks: extra hops per port id.</param>
    /// <param name="mark">Sets the mark (extra hops, 0 for none) on a port id; called when the user marks a line.</param>
    public static NodeViewModel Host(
        string name, IReadOnlyList<UsbNode> controllers, DiagnosticReport report, IReadOnlyDictionary<string, int> extraHops,
        Action<string, int> mark)
    {
        var host = new NodeViewModel(HostId, portId: null, isDisconnected: false, hasLine: false, isExpanded: true, mark);
        host.UpdateHost(name, controllers, report, extraHops);
        return host;
    }

    /// <summary>Brings the host item and its children up to date with <paramref name="controllers"/>, <paramref name="report"/> and the marks.</summary>
    public void UpdateHost(string name, IReadOnlyList<UsbNode> controllers, DiagnosticReport report, IReadOnlyDictionary<string, int> extraHops)
    {
        Text = name;
        IsLast = true;
        var builtIn = TopologyCounts.BuiltIn(controllers).Select(hub => hub.Id).ToHashSet();
        UpdateChildren([.. controllers.Select(controller => (controller, false))], report, extraHops, builtIn, portId: null);
        PathStatus = Worst(Children.Select(child => child.PathStatus));
        CountItemsBelow();
        PaintChildren([]);
    }

    // Top down, once every item knows its path status: the trunk colours and the guide columns.
    private void PaintChildren(IReadOnlyList<string?> childLines)
    {
        TrunkColor = Color(Worst(Children.Select(child => child.PathStatus)));
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            child.Guides = childLines;
            child.TrunkAboveColor = Color(Worst(Children.Skip(i).Select(item => item.PathStatus)));
            child.TrunkBelowColor = i < Children.Count - 1 ? Color(Worst(Children.Skip(i + 1).Select(item => item.PathStatus))) : null;
            child.PaintChildren([.. childLines, child.TrunkBelowColor]);
        }
    }

    private static ConnectionStatus? Worst(IEnumerable<ConnectionStatus?> statuses) => statuses.Max();

    private static string Color(ConnectionStatus? status) => status is { } value ? StatusColors.Hex(value) : StatusColors.Trunk;

    private void Update(
        UsbNode node, DiagnosticReport report, IReadOnlyDictionary<string, int> extraHops, IReadOnlySet<string> builtIn, bool last)
    {
        IsLast = last;
        Text = Describe(node, IsDisconnected);
        isHub = node.Kind == UsbNodeKind.Hub;
        // An unplugged device is no display now.
        IsDisplay = node.IsDisplayLink && !IsDisconnected;
        if (SetField(ref markedHops, PortId == null ? 0 : extraHops.GetValueOrDefault(PortId), nameof(ExtraHops)))
        {
            OnPropertyChanged(nameof(IsUserMarked));
            OnPropertyChanged(nameof(MarkBadge));
        }
        Status = node.Kind is UsbNodeKind.Hub or UsbNodeKind.Device && report.Connections.TryGetValue(node.Id, out var value)
            ? value
            : null;
        Counts = node.Kind == UsbNodeKind.Controller ? ControllerCounts(node, extraHops) : null;

        var wanted = node.Children
            .Where(child => child.Kind != UsbNodeKind.Port || child.Children.Count > 0 || child.Problem != null
                || report.Missing.Any(missing => missing.PortId == child.Id))
            .Select(child => (Node: child, Disconnected: false))
            .ToList();
        if (node.Kind == UsbNodeKind.Port && wanted.Count == 0)
        {
            // A disconnected device is shown at its last port, without what was behind it.
            wanted.AddRange(report.Missing
                .Where(missing => missing.PortId == node.Id)
                .Select(missing => (missing.Node with { Children = [] }, true)));
        }
        UpdateChildren(wanted, report, extraHops, builtIn, node.Kind == UsbNodeKind.Port ? node.Id : null);
        PathStatus = Worst(Children.Select(child => child.PathStatus).Prepend(Status));
        CountItemsBelow();
        SetField(ref displayBelow, Children.Any(child => child.IsDisplay || child.displayBelow), nameof(DisplayNote));
    }

    // Only a collapsed item shows the count, so only a collapsed item notifies when it changes.
    private void CountItemsBelow()
    {
        var count = Children.Sum(child => 1 + child.itemsBelow);
        if (count != itemsBelow)
        {
            itemsBelow = count;
            if (!IsExpanded)
            {
                OnPropertyChanged(nameof(HiddenCount));
                OnPropertyChanged(nameof(CollapsedHint));
            }
        }
    }

    private void UpdateChildren(
        IReadOnlyList<(UsbNode Node, bool Disconnected)> wanted, DiagnosticReport report, IReadOnlyDictionary<string, int> extraHops,
        IReadOnlySet<string> builtIn, string? portId)
    {
        for (var i = 0; i < wanted.Count; i++)
        {
            var (child, disconnected) = wanted[i];
            var childIsLast = i == wanted.Count - 1;
            var existing = Children.FirstOrDefault(item => item.Id == child.Id && item.IsDisconnected == disconnected);
            if (existing == null)
            {
                // A disconnected device has nothing behind it now, so its line cannot be marked.
                var item = new NodeViewModel(child.Id, disconnected ? null : portId, disconnected, hasLine: true, isExpanded: !builtIn.Contains(child.Id), mark);
                item.Update(child, report, extraHops, builtIn, childIsLast);
                Children.Insert(i, item);
                continue;
            }
            var at = Children.IndexOf(existing);
            if (at != i)
            {
                Children.Move(at, i);
            }
            existing.Update(child, report, extraHops, builtIn, childIsLast);
        }
        while (Children.Count > wanted.Count)
        {
            Children.RemoveAt(Children.Count - 1);
        }
    }

    private static string ControllerCounts(UsbNode controller, IReadOnlyDictionary<string, int> extraHops)
    {
        IReadOnlyList<UsbNode> tree = [controller];
        var depths = TopologyCounts.Depths(tree, extraHops).Values;
        return string.Format(CultureInfo.CurrentCulture,
            TopologyCounts.Unreadable(tree).Any() ? Strings.ControllerCountsAtLeast : Strings.ControllerCounts,
            TopologyCounts.HubCount(tree),
            depths.Select(depth => depth.Tier).DefaultIfEmpty(0).Max(),
            depths.Select(depth => depth.Hops).DefaultIfEmpty(0).Max());
    }

    private static string Describe(UsbNode node, bool isDisconnected)
    {
        var name = node.VendorId is { } vendor && node.ProductId is { } product
            ? string.Format(CultureInfo.CurrentCulture, Strings.NodeWithIds, node.Name, vendor, product)
            : node.Name;
        if (isDisconnected)
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.NodeDisconnected, name);
        }
        return node.Problem == null ? name : string.Format(CultureInfo.CurrentCulture, Strings.NodeProblem, name, node.Problem);
    }
}
