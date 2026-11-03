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
/// Only a controller can be collapsed, which hides its tree; the host and every item below a controller are always open
/// (#142). A collapsed controller's name is bold (#100).
/// A DisplayLink device is shown as a display, and every hub above it says it has one (#92).
/// A port the operating system calls USB-C says so; an unknown one says nothing (#114).
/// After the controllers, a "Displays" group lists each active monitor with its name and output type (#133). A monitor
/// that Windows puts in the same container as a USB hub or device is shown under that node instead (#143); no other
/// placement is guessed (#93).
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
    private string? portType;
    private bool isExpanded;
    private bool isController;
    private bool isDisplay;
    private bool displayBelow;
    private bool isHub;
    private int markedHops;
    private readonly Action<string, int> mark;

    private NodeViewModel(string id, string? portId, bool isDisconnected, bool hasLine, Action<string, int> mark)
    {
        Id = id;
        PortId = portId;
        IsDisconnected = isDisconnected;
        this.mark = mark;
        HasLine = hasLine;
        isExpanded = true;
        Children.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasChildren));
            OnPropertyChanged(nameof(CanExpand));
            OnPropertyChanged(nameof(ShowsChildren));
            OnPropertyChanged(nameof(IsCollapsed));
        };
    }

    /// <summary>The id of the host item. Never a USB id: those are the operating system's device paths.</summary>
    public const string HostId = "host";

    /// <summary>The id of the Displays group under the host; its monitors' ids start with "display|".</summary>
    public const string DisplaysId = "displays";

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

    /// <summary>
    /// Whether the children are shown. The user expands and collapses a controller with its chevron, a double-click or
    /// the arrow keys. Every other item stays open: collapsing it is refused here, and the window keeps its row open (#142).
    /// </summary>
    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (!value && !isController)
            {
                return;
            }
            if (SetField(ref isExpanded, value))
            {
                OnPropertyChanged(nameof(ShowsChildren));
                OnPropertyChanged(nameof(IsCollapsed));
            }
        }
    }

    /// <summary>A controller with children: the view shows its expand/collapse control. No other item has one.</summary>
    public bool CanExpand => isController && HasChildren;

    /// <summary>A controller with children that is not expanded: the view shows its name in bold.</summary>
    public bool IsCollapsed => HasChildren && !IsExpanded;

    /// <summary>The children are visible, so the item's own trunk is drawn below it.</summary>
    public bool ShowsChildren => HasChildren && IsExpanded;

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
    /// Before the name on a DisplayLink device: "Extended display: DisplayLink driver required"; after the name on a hub
    /// with one below it: "has a display". Null otherwise.
    /// </summary>
    public string? DisplayNote => IsDisplay ? Strings.DisplayLinkDevice : HasDisplayBelow ? Strings.HubHasDisplay : null;

    /// <summary>A hub with a connected DisplayLink device somewhere below it.</summary>
    public bool HasDisplayBelow => isHub && displayBelow;

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

    /// <summary>"USB-C" on a port the operating system calls USB-C; null for every other item.</summary>
    public string? PortType
    {
        get => portType;
        private set => SetField(ref portType, value);
    }

    /// <summary>For a controller: its hub count, deepest tier and deepest hop. Null for everything else.</summary>
    public string? Counts
    {
        get => counts;
        private set => SetField(ref counts, value);
    }

    public string LineColor => Status is { } value ? StatusColors.Hex(value) : StatusColors.None;

    public string? StatusText => Status is { } value ? StatusColors.Text(value) : null;

    /// <summary>
    /// The host item, the top of the tree without a line, with <paramref name="controllers"/> and then the Displays
    /// group, when there are <paramref name="monitors"/>, as its children.
    /// </summary>
    /// <param name="extraHops">The user's marks: extra hops per port id.</param>
    /// <param name="mark">Sets the mark (extra hops, 0 for none) on a port id; called when the user marks a line.</param>
    public static NodeViewModel Host(
        string name, IReadOnlyList<UsbNode> controllers, DiagnosticReport report, IReadOnlyDictionary<string, int> extraHops,
        Action<string, int> mark, MonitorPlacement monitors)
    {
        var host = new NodeViewModel(HostId, portId: null, isDisconnected: false, hasLine: false, mark);
        host.UpdateHost(name, controllers, report, extraHops, monitors);
        return host;
    }

    /// <summary>
    /// Brings the host item and its children up to date with <paramref name="controllers"/>, <paramref name="report"/>,
    /// the marks and <paramref name="monitors"/>.
    /// </summary>
    public void UpdateHost(
        string name, IReadOnlyList<UsbNode> controllers, DiagnosticReport report, IReadOnlyDictionary<string, int> extraHops,
        MonitorPlacement monitors)
    {
        Text = name;
        IsLast = true;
        // A monitor linked to a USB node is shown under it (#143); the others are in the Displays group. A DisplayLink
        // monitor stays under its USB device (#92): while one is connected, indirect monitors are not listed again.
        // Windows does not say which indirect monitor hangs off which adapter.
        var listed = controllers.Any(HasDisplayLink)
            ? [.. monitors.Unlinked.Where(monitor => monitor.Output != MonitorOutput.Indirect)]
            : monitors.Unlinked;
        var group = Children.FirstOrDefault(item => item.Id == DisplaysId);
        if (group != null && listed.Count == 0)
        {
            Children.Remove(group);
            group = null;
        }
        else if (group == null && listed.Count > 0)
        {
            group = new NodeViewModel(DisplaysId, portId: null, isDisconnected: false, hasLine: true, mark);
            Children.Add(group);
        }
        var trailing = group == null ? 0 : 1;
        UpdateChildren([.. controllers.Select(controller => (controller, false))], report, extraHops, monitors.Linked, portId: null, trailing, trailing > 0);
        if (group != null)
        {
            group.Text = Strings.DisplaysGroup;
            group.IsLast = true;
            group.UpdateMonitors(listed, 0);
        }
        PathStatus = Worst(Children.Select(child => child.PathStatus));
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

    private static bool HasDisplayLink(UsbNode node) => node.IsDisplayLink || node.Children.Any(HasDisplayLink);

    // The Displays group or a USB node's linked monitors (#143): one item per monitor from index start on, after the
    // USB children, in the operating system's order. An item is kept while its monitor is.
    private void UpdateMonitors(IReadOnlyList<DisplayMonitor> monitors, int start)
    {
        for (var i = 0; i < monitors.Count; i++)
        {
            var monitor = monitors[i];
            var id = MonitorPrefix + monitor.Id;
            var item = Children.Skip(start).FirstOrDefault(child => child.Id == id);
            if (item == null)
            {
                item = new NodeViewModel(id, portId: null, isDisconnected: false, hasLine: true, mark);
                Children.Insert(start + i, item);
            }
            else if (Children.IndexOf(item) is var at && at != start + i)
            {
                Children.Move(at, start + i);
            }
            item.Text = string.Format(CultureInfo.CurrentCulture, Strings.MonitorItem, monitor.Name ?? Strings.MonitorUnnamed, OutputText(monitor.Output));
            item.IsLast = i == monitors.Count - 1;
        }
        while (Children.Count > start + monitors.Count)
        {
            Children.RemoveAt(Children.Count - 1);
        }
    }

    private const string MonitorPrefix = "display|";

    // The Displays group or a monitor item: kept after the USB children.
    private bool IsMonitorItem => Id == DisplaysId || Id.StartsWith(MonitorPrefix, StringComparison.Ordinal);

    private static string OutputText(MonitorOutput output) => output switch
    {
        MonitorOutput.Internal => Strings.OutputInternal,
        MonitorOutput.Hdmi => Strings.OutputHdmi,
        MonitorOutput.DisplayPort => Strings.OutputDisplayPort,
        MonitorOutput.UsbTunnel => Strings.OutputUsbTunnel,
        MonitorOutput.Dvi => Strings.OutputDvi,
        MonitorOutput.Vga => Strings.OutputVga,
        MonitorOutput.Indirect => Strings.OutputIndirect,
        MonitorOutput.Wireless => Strings.OutputWireless,
        MonitorOutput.Virtual => Strings.OutputVirtual,
        _ => Strings.OutputOther,
    };

    private void Update(
        UsbNode node, DiagnosticReport report, IReadOnlyDictionary<string, int> extraHops,
        IReadOnlyDictionary<string, IReadOnlyList<DisplayMonitor>> linked, bool last)
    {
        IsLast = last;
        Text = Describe(node, IsDisconnected);
        isHub = node.Kind == UsbNodeKind.Hub;
        isController = node.Kind == UsbNodeKind.Controller;
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
        PortType = node.Kind == UsbNodeKind.Port && node.UsbC == true ? Strings.UsbCPort : null;

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
        var monitors = linked.GetValueOrDefault(node.Id) ?? [];
        UpdateChildren(wanted, report, extraHops, linked, node.Kind == UsbNodeKind.Port ? node.Id : null,
            Children.Count(child => child.IsMonitorItem), monitors.Count > 0);
        UpdateMonitors(monitors, wanted.Count);
        PathStatus = Worst(Children.Select(child => child.PathStatus).Prepend(Status));
        if (SetField(ref displayBelow, Children.Any(child => child.IsDisplay || child.displayBelow), nameof(DisplayNote)))
        {
            OnPropertyChanged(nameof(HasDisplayBelow));
        }
    }

    private void UpdateChildren(
        IReadOnlyList<(UsbNode Node, bool Disconnected)> wanted, DiagnosticReport report, IReadOnlyDictionary<string, int> extraHops,
        IReadOnlyDictionary<string, IReadOnlyList<DisplayMonitor>> linked, string? portId, int trailing, bool followed)
    {
        // The trailing monitor items (the host's Displays group, a USB node's linked monitors) stay after the USB children;
        // when there are any, the last USB child is not the last item.
        for (var i = 0; i < wanted.Count; i++)
        {
            var (child, disconnected) = wanted[i];
            var childIsLast = i == wanted.Count - 1 && !followed;
            var existing = Children.FirstOrDefault(item => item.Id == child.Id && item.IsDisconnected == disconnected);
            if (existing == null)
            {
                // A disconnected device has nothing behind it now, so its line cannot be marked.
                var item = new NodeViewModel(child.Id, disconnected ? null : portId, disconnected, hasLine: true, mark);
                item.Update(child, report, extraHops, linked, childIsLast);
                Children.Insert(i, item);
                continue;
            }
            var at = Children.IndexOf(existing);
            if (at != i)
            {
                Children.Move(at, i);
            }
            existing.Update(child, report, extraHops, linked, childIsLast);
        }
        while (Children.Count > wanted.Count + trailing)
        {
            Children.RemoveAt(wanted.Count);
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
