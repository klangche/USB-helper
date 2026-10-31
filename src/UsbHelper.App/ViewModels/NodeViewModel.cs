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
/// A port marked with extra hops for an active cable or extender shows the mark after its name.
/// A hub built into the computer starts collapsed, everything else open (#68); the item keeps the user's choice
/// across updates.
/// </summary>
public sealed class NodeViewModel : ObservableObject
{
    private string text = "";
    private ConnectionStatus? status;
    private IReadOnlyList<bool> guides = [];
    private bool isLast;
    private string? counts;
    private bool isExpanded;

    private NodeViewModel(string id, bool isPort, bool isDisconnected, bool hasLine, bool isExpanded)
    {
        Id = id;
        IsPort = isPort;
        IsDisconnected = isDisconnected;
        HasLine = hasLine;
        this.isExpanded = isExpanded;
        Children.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasChildren));
            OnPropertyChanged(nameof(ShowsChildren));
            OnPropertyChanged(nameof(CollapsedHint));
        };
    }

    /// <summary>The id of the host item. Never a USB id: those are the operating system's device paths.</summary>
    public const string HostId = "host";

    public string Id { get; }

    /// <summary>A hub port; only ports can be marked with extra hops.</summary>
    public bool IsPort { get; }

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
                OnPropertyChanged(nameof(CollapsedHint));
            }
        }
    }

    /// <summary>The children are visible, so the item's own trunk is drawn below it.</summary>
    public bool ShowsChildren => HasChildren && IsExpanded;

    /// <summary>After the name of a collapsed item with children: how to show them. Null otherwise.</summary>
    public string? CollapsedHint => HasChildren && !IsExpanded ? Strings.CollapsedHint : null;

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

    public bool HasStatus => Status != null;

    /// <summary>The result word on the right, in <see cref="LineColor"/>; null for controllers, root hubs and ports.</summary>
    public string? StatusWord => Status is { } value ? StatusColors.Word(value) : null;

    /// <summary>The elbow from the parent: the status colour for a hub or device, the trunk colour otherwise.</summary>
    public string ElbowColor => Status is { } value ? StatusColors.Hex(value) : StatusColors.Trunk;

    /// <summary>False for the host, the top of the tree.</summary>
    public bool HasLine { get; }

    /// <summary>One entry per ancestor column left of the elbow: true where that ancestor's trunk continues past this row.</summary>
    public IReadOnlyList<bool> Guides
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
    public static NodeViewModel Host(
        string name, IReadOnlyList<UsbNode> controllers, DiagnosticReport report, IReadOnlyDictionary<string, int> extraHops)
    {
        var host = new NodeViewModel(HostId, isPort: false, isDisconnected: false, hasLine: false, isExpanded: true);
        host.UpdateHost(name, controllers, report, extraHops);
        return host;
    }

    /// <summary>Brings the host item and its children up to date with <paramref name="controllers"/>, <paramref name="report"/> and the marks.</summary>
    public void UpdateHost(string name, IReadOnlyList<UsbNode> controllers, DiagnosticReport report, IReadOnlyDictionary<string, int> extraHops)
    {
        Text = name;
        IsLast = true;
        var builtIn = TopologyCounts.BuiltIn(controllers).Select(hub => hub.Id).ToHashSet();
        UpdateChildren([.. controllers.Select(controller => (controller, false))], report, extraHops, builtIn, []);
    }

    private void Update(
        UsbNode node, DiagnosticReport report, IReadOnlyDictionary<string, int> extraHops, IReadOnlySet<string> builtIn, IReadOnlyList<bool> lines,
        bool last)
    {
        Guides = lines;
        IsLast = last;
        Text = extraHops.TryGetValue(node.Id, out var hops)
            ? string.Format(CultureInfo.CurrentCulture, Strings.PortMark, Describe(node, IsDisconnected), hops)
            : Describe(node, IsDisconnected);
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
        // Below this item, its own trunk continues past every child but the last.
        UpdateChildren(wanted, report, extraHops, builtIn, [.. lines, !last]);
    }

    private void UpdateChildren(
        IReadOnlyList<(UsbNode Node, bool Disconnected)> wanted, DiagnosticReport report, IReadOnlyDictionary<string, int> extraHops,
        IReadOnlySet<string> builtIn, IReadOnlyList<bool> childLines)
    {
        for (var i = 0; i < wanted.Count; i++)
        {
            var (child, disconnected) = wanted[i];
            var childIsLast = i == wanted.Count - 1;
            var existing = Children.FirstOrDefault(item => item.Id == child.Id && item.IsDisconnected == disconnected);
            if (existing == null)
            {
                Children.Insert(i, Create(child, report, extraHops, builtIn, disconnected, childLines, childIsLast));
                continue;
            }
            var at = Children.IndexOf(existing);
            if (at != i)
            {
                Children.Move(at, i);
            }
            existing.Update(child, report, extraHops, builtIn, childLines, childIsLast);
        }
        while (Children.Count > wanted.Count)
        {
            Children.RemoveAt(Children.Count - 1);
        }
    }

    private static NodeViewModel Create(
        UsbNode node, DiagnosticReport report, IReadOnlyDictionary<string, int> extraHops, IReadOnlySet<string> builtIn, bool isDisconnected,
        IReadOnlyList<bool> lines, bool isLast)
    {
        var item = new NodeViewModel(node.Id, node.Kind == UsbNodeKind.Port, isDisconnected, hasLine: true, isExpanded: !builtIn.Contains(node.Id));
        item.Update(node, report, extraHops, builtIn, lines, isLast);
        return item;
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
