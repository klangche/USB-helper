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
/// system does not show (#76): "+N hubs" adds N hubs on its bus and N tiers behind it, "+N tiers" only N tiers (#307).
/// Only a controller can be collapsed, which hides its tree; the host and every item below a controller are always open
/// (#142). A collapsed controller's name is bold (#100).
/// A DisplayLink device is shown as a display, and every hub above it says it has one (#92).
/// A port the operating system calls USB-C says so; an unknown one says nothing (#114).
/// After the controllers, a "Displays" group lists each active monitor with its name and output type (#133). A monitor
/// that Windows puts in the same container as a USB hub or device is shown under that node instead (#143); no other
/// placement is guessed (#93).
/// The window shows <see cref="Rows"/>, where a port with something on it is no row of its own: its hub or device
/// carries the port number and the USB-C badge (#264). <see cref="Children"/> keeps the port items, for the tree as
/// text, the log, the export and the report.
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
    private CountsText? counts;
    private string? portType;
    private bool isExpanded;
    private bool isController;
    private bool isDisplay;
    private bool displayBelow;
    private bool isHub;
    private CableMark? cableMark;
    private string? depthText;
    private bool isLastRow;
    private bool isFolded;
    private string? portNumber;
    private string? portName;
    private string? portBadge;
    private string? pairedPortId;
    private bool isHalf;
    private string? note;
    private IReadOnlyList<string> groupPortIds = [];
    private IReadOnlyList<string> groupControllerIds = [];
    private readonly Action<string, CableMark?> mark;

    private NodeViewModel(string id, string? portId, bool isDisconnected, bool hasLine, Action<string, CableMark?> mark)
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

    /// <summary>
    /// The choices for a mark, in the menu's order (#307): none, then <see cref="CableMark.Choices"/>, +1 to +3 hubs and
    /// +1 to +3 tiers. "+2 hubs" says it is an active extension.
    /// </summary>
    public static IReadOnlyList<string> MarkChoices { get; } =
        [Strings.MarkNone, .. CableMark.Choices.Select(choice => choice.IsActiveExtension
            ? string.Format(CultureInfo.CurrentCulture, Strings.MarkActiveExtension, MarkText(choice))
            : MarkText(choice))];

    /// <summary>What a mark adds, as its badge says: "+1 hub", "+2 hubs", "+3 tiers" (#307).</summary>
    public static string MarkText(CableMark mark)
    {
        if (mark.Metres is { } metres)
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.MarkCable, metres, mark.Bus == UsbBus.Usb3 ? Strings.GuideLineUsb3 : Strings.GuideLineUsb2);
        }
        string Hubs(int number) => number == 1 ? Strings.MarkHubOne : string.Format(CultureInfo.CurrentCulture, Strings.MarkHubs, number);
        string Tiers(int number) => number == 1 ? Strings.MarkTierOne : string.Format(CultureInfo.CurrentCulture, Strings.MarkTiers, number);
        // Hubs and more tiers than hubs: only from a hand-edited setup.json.
        return mark.Hubs == 0 ? Tiers(mark.Tiers)
            : mark.Hubs == mark.Tiers ? Hubs(mark.Hubs)
            : string.Format(CultureInfo.CurrentCulture, Strings.MarkHubsAndTiers, Hubs(mark.Hubs), Tiers(mark.Tiers));
    }

    /// <summary>Sets the mark from the menu: the parameter is the place in <see cref="MarkChoices"/>, 0 for none.</summary>
    public System.Windows.Input.ICommand SetMark => field ??= new IndexCommand(index => MarkIndex = index);

    private sealed class IndexCommand(Action<int> run) : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => run(Convert.ToInt32(parameter, CultureInfo.InvariantCulture));
    }

    /// <summary>For a connected hub or device: the port its line comes from, where a mark is kept. Null otherwise.</summary>
    public string? PortId { get; }

    /// <summary>True when the line to this item can be marked as an active cable or extender.</summary>
    public bool CanMark => PortId != null;

    /// <summary>
    /// The user's mark on the line to this item for an active cable or extender, null for none (#307). Setting it
    /// re-evaluates everything; it is ignored when the line cannot be marked.
    /// </summary>
    public CableMark? Mark
    {
        get => cableMark;
        set
        {
            if (PortId != null && value != cableMark)
            {
                mark(PortId, value);
                // #370: a hub shown with its other half is on two ports, one per bus; its line carries both.
                if (pairedPortId != null)
                {
                    mark(pairedPortId, value);
                }
            }
        }
    }

    /// <summary>
    /// The mark as its place in <see cref="MarkChoices"/>, for the menu: 0 for none; -1 for a mark that is not one of
    /// the choices (from a hand-edited setup.json), which setting -1 keeps.
    /// </summary>
    public int MarkIndex
    {
        get => cableMark == null ? 0 : CableMark.Choices.ToList().IndexOf(cableMark) is var at and >= 0 ? at + 1 : -1;
        set
        {
            if (value >= 0 && value < MarkChoices.Count)
            {
                Mark = value == 0 ? null : CableMark.Choices[value - 1];
            }
        }
    }

    /// <summary>The length typed for an active cable (#331), in metres; the mark is set with <see cref="SetCable"/>.</summary>
    public decimal? CableMetres
    {
        get => field ?? (cableMark?.Metres is { } metres ? (decimal)metres : 5);
        set;
    }

    /// <summary>The active cable's bus for <see cref="SetCable"/>: 0 for USB 2, 1 for USB 3; the marked cable's at first.</summary>
    public int CableBusIndex
    {
        get => cableBusIndex ?? (cableMark?.Bus == UsbBus.Usb3 ? 1 : 0);
        set => cableBusIndex = value;
    }

    private int? cableBusIndex;

    /// <summary>The choices for <see cref="CableBusIndex"/>.</summary>
    public static IReadOnlyList<string> CableBuses { get; } = [Strings.GuideLineUsb2, Strings.GuideLineUsb3];

    /// <summary>Marks the line as an active cable of <see cref="CableMetres"/> on the chosen bus (#331).</summary>
    public System.Windows.Input.ICommand SetCable => field ??= new IndexCommand(_ =>
    {
        if (CableMetres is { } metres && metres > 0)
        {
            Mark = CableMark.ActiveCable((double)metres, CableBusIndex == 1 ? UsbBus.Usb3 : UsbBus.Usb2);
        }
    });

    /// <summary>The user marked this line; the view draws it dashed with <see cref="MarkBadge"/>.</summary>
    public bool IsUserMarked => cableMark != null;

    /// <summary>What the mark adds, "+2 hubs" or "+1 tier", on a marked line; null otherwise.</summary>
    public string? MarkBadge => cableMark is { } value ? MarkText(value) : null;

    /// <summary>A device seen earlier, shown at its last port.</summary>
    public bool IsDisconnected { get; }

    public ObservableCollection<NodeViewModel> Children { get; } = [];

    /// <summary>
    /// The children as the window shows them (#264): in place of a port with something on it, the hubs and devices on
    /// that port. A port with a problem keeps its own row, so the problem stays visible.
    /// </summary>
    public ObservableCollection<NodeViewModel> Rows { get; } = [];

    public bool HasChildren => Children.Count > 0;

    /// <summary>
    /// The children as the tree as text and the report write them (#370): the halves of a port row are not written, as
    /// the window does not show them; what is on them is. Each with whether it is the last one written.
    /// </summary>
    internal IReadOnlyList<(NodeViewModel Item, bool IsLast)> WrittenChildren
    {
        get
        {
            var written = Children.SelectMany(child => child.isHalf ? child.Children : [child]).ToList();
            return [.. written.Select((item, i) => (item, i == written.Count - 1))];
        }
    }

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

    /// <summary>True for a connected device: its kind can be selected by hand (#276).</summary>
    public bool IsDevice
    {
        get => isDevice;
        private set => SetField(ref isDevice, value);
    }

    private bool isDevice;

    /// <summary>The device's kind, reported by the system or selected by hand (#276); null for everything else.</summary>
    public UsbDeviceKind? DeviceKind
    {
        get => deviceKind;
        private set
        {
            if (SetField(ref deviceKind, value))
            {
                OnPropertyChanged(nameof(KindSymbol));
                OnPropertyChanged(nameof(KindTip));
            }
        }
    }

    private UsbDeviceKind? deviceKind;

    /// <summary>True when <see cref="DeviceKind"/> was selected by hand, not reported by the system (#276).</summary>
    public bool KindByUser
    {
        get => kindByUser;
        private set
        {
            if (SetField(ref kindByUser, value))
            {
                OnPropertyChanged(nameof(KindSymbol));
                OnPropertyChanged(nameof(KindTip));
            }
        }
    }

    private bool kindByUser;

    /// <summary>
    /// The symbol in front of a camera's or audio device's name (#276): a camera, a microphone, or both; a hand-selected
    /// kind is marked with "✎", also "Neither" on its own. Null for every other row.
    /// </summary>
    public string? KindSymbol => DeviceKind switch
    {
        UsbDeviceKind.Camera => "📷",
        UsbDeviceKind.Audio => "🎙",
        UsbDeviceKind.CameraAndAudio => "📷🎙",
        _ => null,
    } is { } symbol ? KindByUser ? symbol + "✎" : symbol : KindByUser ? "✎" : null;

    /// <summary>The limits that apply to this kind and their source, from the guide in use (#276); set by the main view model.</summary>
    public string? KindLimits
    {
        get => kindLimits;
        internal set
        {
            if (SetField(ref kindLimits, value))
            {
                OnPropertyChanged(nameof(KindTip));
            }
        }
    }

    private string? kindLimits;

    /// <summary>
    /// Hovering the symbol (#276): what the device is, how the app knows, why it is judged more strictly, then the limits
    /// that apply and their source.
    /// </summary>
    public string? KindTip => KindSymbol == null ? null : DeviceKind == UsbDeviceKind.Neither
        ? Strings.KindNeither + Environment.NewLine + Strings.KindSelectedByHand
        : string.Join(Environment.NewLine, new[]
        {
            DeviceKind switch { UsbDeviceKind.Camera => Strings.KindCamera, UsbDeviceKind.Audio => Strings.KindAudio, _ => Strings.KindCameraAndAudio },
            KindByUser ? Strings.KindSelectedByHand : Strings.KindReported,
            Strings.KindWhyStricter,
            KindLimits,
        }.Where(line => line != null));

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
                OnPropertyChanged(nameof(RowTip));
                OnPropertyChanged(nameof(StatusWord));
                OnPropertyChanged(nameof(RowStatusWord));
                OnPropertyChanged(nameof(RowTint));
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

    /// <summary>A warning that is not a status (#296): "⚠ no MST" on a display that is duplicated; null for none.</summary>
    public string? WarningBadge
    {
        get;
        private set
        {
            if (SetField(ref field, value))
            {
                OnPropertyChanged(nameof(LineColor));
                OnPropertyChanged(nameof(ElbowColor));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(RowTip));
            }
        }
    }

    /// <summary>The result word on the right, in <see cref="LineColor"/>; null for controllers, root hubs and ports.</summary>
    public string? StatusWord => Status is { } value ? StatusColors.Word(value) : null;

    /// <summary>The result word on the window's row, shown only when the item is not stable (#264); null otherwise.</summary>
    public string? RowStatusWord => Status is { } value && value != ConnectionStatus.Green ? StatusColors.Word(value) : null;

    /// <summary>The light background of the window's row of an item that is not stable, in its status colour; none otherwise.</summary>
    public string RowTint => IsOnHoveredPath ? StatusColors.HoveredPath : Status is { } value && value != ConnectionStatus.Green ? StatusColors.Tint(value) : StatusColors.None;

    /// <summary>
    /// The elbow from the parent: the item's own status colour for a hub or device; for a controller, root hub or port
    /// the worst status below it.
    /// </summary>
    public string ElbowColor => WarningBadge != null ? StatusColors.Hex(ConnectionStatus.Orange) : Color(Status ?? PathStatus);

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

    /// <summary>The last of its parent's <see cref="Rows"/>: in the window its elbow ends the parent's trunk.</summary>
    public bool IsLastRow
    {
        get => isLastRow;
        private set => SetField(ref isLastRow, value);
    }

    /// <summary>"USB-C" on a port the operating system calls USB-C; null for every other item.</summary>
    public string? PortType
    {
        get => portType;
        private set => SetField(ref portType, value);
    }

    /// <summary>
    /// For a hub or device whose port is folded into its row: the port's number, shown in a small box before the name;
    /// the port's name when it has no number. Null otherwise.
    /// </summary>
    public string? PortNumber
    {
        get => portNumber;
        private set => SetField(ref portNumber, value);
    }

    /// <summary>The name of the folded port, such as "Port 2": the tool tip of <see cref="PortNumber"/>. Null otherwise.</summary>
    public string? PortName
    {
        get => portName;
        private set => SetField(ref portName, value);
    }

    /// <summary>
    /// The port this row's port box stands for (#266): the folded port of a hub or device on one; null otherwise. Hovering it shows <see cref="PortHover"/> and highlights the path.
    /// </summary>
    public string? HoverPortId
    {
        get => hoverPortId;
        private set => SetField(ref hoverPortId, value);
    }

    private string? hoverPortId;

    /// <summary>The hover of <see cref="HoverPortId"/>, made when the pointer enters it (#266).</summary>
    public PortHover? PortHover
    {
        get => portHover;
        internal set => SetField(ref portHover, value);
    }

    private PortHover? portHover;

    /// <summary>True while a hovered port box is on or below this row: the line from the computer to that port (#266).</summary>
    public bool IsOnHoveredPath
    {
        get => isOnHoveredPath;
        internal set
        {
            if (SetField(ref isOnHoveredPath, value))
            {
                OnPropertyChanged(nameof(RowTint));
            }
        }
    }

    private bool isOnHoveredPath;

    /// <summary>
    /// "USB-C" on the window's row: on a hub or device whose folded port the operating system calls USB-C, and on such a
    /// port that keeps its own row. Null otherwise.
    /// </summary>
    public string? PortBadge
    {
        get => portBadge;
        private set => SetField(ref portBadge, value);
    }

    /// <summary>
    /// As text: for a controller, its hub count, with the hubs per bus in brackets (USB 2, USB 3), any USB4 routers, and its deepest
    /// tier (#217, #298, #359). For the host: the same over all controllers, hubs summed (#208). Null for everything else
    /// and for a controller with nothing connected.
    /// </summary>
    public string? Counts => counts?.ToString();

    /// <summary>The counts up to the USB2 number: the hub count, plus " (" when a bus is known.</summary>
    public string? CountsHead => counts?.Head;

    /// <summary>The USB2 hub count in the brackets; null when no bus is known.</summary>
    public string? CountsUsb2 => counts?.Usb2;

    /// <summary>The USB3 hub count in the brackets; null when no bus is known.</summary>
    public string? CountsUsb3 => counts?.Usb3;


    /// <summary>The text, and which bus each number in the brackets is.</summary>
    public string? CountsTip => counts?.Tip;

    /// <summary>The counts after the USB3 number.</summary>
    public string? CountsTail => counts?.Tail;

    public bool HasCountsPerBus => counts?.Usb2 != null;

    private void SetCounts(CountsText? value)
    {
        if (counts == value)
        {
            return;
        }
        counts = value;
        foreach (var name in new[] { nameof(Counts), nameof(CountsHead), nameof(CountsUsb2), nameof(CountsUsb3), nameof(CountsTip), nameof(CountsTail), nameof(HasCountsPerBus) })
        {
            OnPropertyChanged(name);
        }
    }

    // A warning (#296) draws the line orange although the status stays stable: it can be sorted, it is not unstable.
    public string LineColor => WarningBadge != null ? StatusColors.Hex(ConnectionStatus.Orange)
        : Status is { } value ? StatusColors.Hex(value) : StatusColors.None;

    public string? StatusText => WarningBadge != null ? Strings.NoMstTip : Status is { } value ? StatusColors.Text(value) : null;

    /// <summary>
    /// For a counted hub or device: "Tier N" (#195), the user's marks counted. Null for everything else.
    /// </summary>
    public string? DepthText
    {
        get => depthText;
        private set
        {
            if (SetField(ref depthText, value))
            {
                OnPropertyChanged(nameof(RowTip));
            }
        }
    }

    /// <summary>The row's tooltip: the status and, below it, the depth.</summary>
    public string? RowTip => string.Join("\n", new[] { StatusText, DepthText, note }.OfType<string>()) is { Length: > 0 } tip ? tip : null;

    /// <summary>For a row of the tree by port (#370): the ids of the ports under it, so hovering one of them highlights it too.</summary>
    public IReadOnlyList<string> GroupPortIds => groupPortIds;

    /// <summary>For a row of the tree by port (#370): the controllers its ports are on, for opening the selected one's.</summary>
    public IReadOnlyList<string> GroupControllerIds => groupControllerIds;

    private void SetNote(string? value)
    {
        SetField(ref note, value, nameof(RowTip));
    }

    /// <summary>
    /// The host item, the top of the tree without a line, with a row per group of <paramref name="layout"/> (the tree by
    /// port, #370) and then the Displays group, when there are <paramref name="monitors"/>, as its children.
    /// </summary>
    /// <param name="controllers">The tree itself, for the counts.</param>
    /// <param name="marks">The user's marks per port id.</param>
    /// <param name="mark">Sets the mark (null for none) on a port id; called when the user marks a line.</param>
    public static NodeViewModel Host(
        string name, IReadOnlyList<UsbNode> controllers, PortTree layout, DiagnosticReport report, IReadOnlyDictionary<string, CableMark> marks,
        Action<string, CableMark?> mark, MonitorPlacement monitors)
    {
        var host = new NodeViewModel(HostId, portId: null, isDisconnected: false, hasLine: false, mark);
        host.UpdateHost(name, controllers, layout, report, marks, monitors);
        return host;
    }

    /// <summary>
    /// Brings the host item and its children up to date with <paramref name="controllers"/> shown by port as in
    /// <paramref name="layout"/>, <paramref name="report"/>, the marks and <paramref name="monitors"/>.
    /// </summary>
    public void UpdateHost(
        string name, IReadOnlyList<UsbNode> controllers, PortTree layout, DiagnosticReport report, IReadOnlyDictionary<string, CableMark> marks,
        MonitorPlacement monitors)
    {
        Text = name;
        IsLast = true;
        SetCounts(TreeCounts(controllers, marks));
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
        UpdateGroups(layout, report, marks, new Context(monitors.Linked, layout), group);
        if (group != null)
        {
            group.Text = Strings.DisplaysGroup;
            group.IsLast = true;
            group.UpdateMonitors(listed, 0, report);
            group.UpdateRows();
            group.PathStatus = Worst(group.Children.Select(child => child.PathStatus));
        }
        PathStatus = Worst(Children.Select(child => child.PathStatus));
        UpdateRows();
        PaintChildren([]);
    }

    // What the tree's parts need besides the report and the marks: the monitors linked to USB nodes (#143) and the tree
    // by port (#370), for the hubs shown with their other half and the bus of each port.
    private sealed record Context(IReadOnlyDictionary<string, IReadOnlyList<DisplayMonitor>> Linked, PortTree Layout);

    // A port is shown when something is on it, it has a problem, or a disconnected device is kept at it.
    private static bool Shown(UsbNode port, DiagnosticReport report) =>
        port.Children.Count > 0 || port.Problem != null || report.Missing.Any(missing => missing.PortId == port.Id);

    // The host's rows by port (#370), in the layout's order, before the Displays group: a group with nothing to show
    // has no row. Changed in place, so a row that stays keeps its place, its selection and whether it is open.
    private void UpdateGroups(PortTree layout, DiagnosticReport report, IReadOnlyDictionary<string, CableMark> marks, Context context,
        NodeViewModel? displays)
    {
        var shown = new List<NodeViewModel>();
        foreach (var group in layout.Groups)
        {
            var item = Children.FirstOrDefault(child => child.Id == group.Id)
                ?? new NodeViewModel(group.Id, portId: null, isDisconnected: false, hasLine: true, mark);
            item.UpdateGroup(group, report, marks, context);
            if (item.HasChildren || group.Reasons.Count > 0)
            {
                shown.Add(item);
            }
        }
        for (var i = 0; i < shown.Count; i++)
        {
            shown[i].IsLast = i == shown.Count - 1 && displays == null;
            var at = Children.IndexOf(shown[i]);
            if (at < 0)
            {
                Children.Insert(i, shown[i]);
            }
            else if (at != i)
            {
                Children.Move(at, i);
            }
        }
        while (Children.Count > shown.Count + (displays == null ? 0 : 1))
        {
            Children.RemoveAt(shown.Count);
        }
    }

    // A row of the tree by port (#370): a computer port, its halves' hubs and devices below it, after its USB4 routers
    // when it has any; the built-in devices; or what cannot be placed, with why in its tool tip.
    private void UpdateGroup(PortGroup group, DiagnosticReport report, IReadOnlyDictionary<string, CableMark> marks, Context context)
    {
        Text = group.Kind switch { PortGroupKind.BuiltIn => Strings.GroupBuiltIn, PortGroupKind.Other => Strings.GroupOther, _ => group.Name };
        isController = true; // Opened and closed as a controller was before.
        groupPortIds = [.. group.Nodes.Where(node => node.Kind == UsbNodeKind.Port).Select(node => node.Id)];
        groupControllerIds = group.ControllerIds;
        var port = group.Kind == PortGroupKind.Port ? group.Nodes[0] : null;
        HoverPortId = port?.Id;
        PortNumber = port == null ? null : TrailingNumber(group.Name) ?? group.Name;
        PortName = port == null ? null : group.Name;
        PortBadge = group.UsbC == true ? Strings.UsbCPort : null;
        PortType = PortBadge;
        SetNote(group.Reasons.Count == 0 ? null : string.Join("\n", group.Reasons));
        var wanted = group.Nodes
            .Where(node => node.Kind != UsbNodeKind.Port || Shown(node, report))
            .Select(node => (Node: node, Disconnected: false))
            .ToList();
        var holder = this;
        if (group.Router is { } router)
        {
            holder = Children.FirstOrDefault(child => child.Id == router.Id);
            if (holder == null)
            {
                Children.Clear();
                holder = new NodeViewModel(router.Id, portId: null, isDisconnected: false, hasLine: true, mark);
                Children.Add(holder);
            }
            holder.Text = router.Names is { } names
                ? string.Format(CultureInfo.CurrentCulture, Strings.NodeUsb4Dock, names, router.Depth)
                : string.Format(CultureInfo.CurrentCulture, Strings.NodeUsb4Router, router.Depth);
            holder.IsLast = true;
        }
        else if (Children.Any(child => child.Id.StartsWith(RouterPrefix, StringComparison.Ordinal)))
        {
            Children.Clear();
        }
        holder.UpdateChildren(wanted, report, marks, context, portId: null, trailing: 0, followed: false);
        if (!ReferenceEquals(holder, this))
        {
            holder.UpdateRows();
            holder.PathStatus = Worst(holder.Children.Select(child => child.PathStatus));
        }
        if (group.Router != null && !holder.HasChildren)
        {
            Children.Clear(); // A router with nothing below it shows no port.
        }
        UpdateRows();
        PathStatus = Worst(Children.Select(child => child.PathStatus));
    }

    private const string RouterPrefix = "usb4|";

    // "1" for "USB-C port 1": the port box shows the number, the row the name.
    private static string? TrailingNumber(string name) =>
        name.LastIndexOf(' ') is var at && at >= 0 && int.TryParse(name.AsSpan(at + 1), NumberStyles.None, CultureInfo.InvariantCulture, out _)
            ? name[(at + 1)..]
            : null;

    // Top down, once every item knows its path status: the trunk colours and the guide columns of the window's rows.
    private void PaintChildren(IReadOnlyList<string?> childLines)
    {
        TrunkColor = Color(Worst(Rows.Select(child => child.PathStatus)));
        for (var i = 0; i < Rows.Count; i++)
        {
            var child = Rows[i];
            child.Guides = childLines;
            child.IsLastRow = i == Rows.Count - 1;
            child.TrunkAboveColor = Color(Worst(Rows.Skip(i).Select(item => item.PathStatus)));
            child.TrunkBelowColor = i < Rows.Count - 1 ? Color(Worst(Rows.Skip(i + 1).Select(item => item.PathStatus))) : null;
            child.PaintChildren([.. childLines, child.TrunkBelowColor]);
        }
    }

    // The window's rows, once the children are up to date: each child, or what is on it when it is a folded port. Changed
    // in place, so a row that stays keeps its place and its selection.
    private void UpdateRows()
    {
        var wanted = new List<NodeViewModel>();
        foreach (var child in Children)
        {
            if (child.isFolded)
            {
                wanted.AddRange(child.Children);
            }
            else
            {
                wanted.Add(child);
            }
        }
        for (var i = 0; i < wanted.Count; i++)
        {
            var at = Rows.IndexOf(wanted[i]);
            if (at < 0)
            {
                Rows.Insert(i, wanted[i]);
            }
            else if (at != i)
            {
                Rows.Move(at, i);
            }
        }
        while (Rows.Count > wanted.Count)
        {
            Rows.RemoveAt(Rows.Count - 1);
        }
    }

    private static ConnectionStatus? Worst(IEnumerable<ConnectionStatus?> statuses) => statuses.Max();

    private static string Color(ConnectionStatus? status) => status is { } value ? StatusColors.Hex(value) : StatusColors.Trunk;

    private static bool HasDisplayLink(UsbNode node) => node.IsDisplayLink || node.Children.Any(HasDisplayLink);

    // The Displays group or a USB node's linked monitors (#143): one item per monitor from index start on, after the
    // USB children, in the operating system's order. An item is kept while its monitor is.
    // A monitor's status comes from the display rules, such as "No MST" (#144); none when no rule judges it.
    private void UpdateMonitors(IReadOnlyList<DisplayMonitor> monitors, int start, DiagnosticReport report)
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
            item.Status = report.Connections.TryGetValue(monitor.Id, out var status) ? status : null;
            item.WarningBadge = report.NoMstMonitors.Contains(monitor.Id) ? Strings.NoMstBadge : null;
            item.PathStatus = item.WarningBadge != null ? ConnectionStatus.Orange : item.Status; // The line to it is orange too.
        }
        while (Children.Count > start + monitors.Count)
        {
            Children.RemoveAt(Children.Count - 1);
        }
    }

    private const string MonitorPrefix = "display|";

    // The Displays group or a monitor item: kept after the USB children.
    private bool IsMonitorItem => Id == DisplaysId || Id.StartsWith(MonitorPrefix, StringComparison.Ordinal);

    internal static string OutputText(MonitorOutput output) => output switch
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

    private void Update(UsbNode node, DiagnosticReport report, IReadOnlyDictionary<string, CableMark> marks, Context context, bool last)
    {
        IsLast = last;
        // #370: a hub shown with its other half is judged by the worse half, and its line is on both halves' ports.
        var half = context.Layout.OtherHalves.GetValueOrDefault(node.Id);
        pairedPortId = half?.PortId;
        var bus = half != null ? Strings.BusBoth
            : context.Layout.Buses.TryGetValue(node.Id, out var known) ? (known == UsbBus.Usb3 ? "USB 3" : "USB 2")
            : null;
        Text = Describe(node, IsDisconnected, bus);
        IsDevice = node.Kind == UsbNodeKind.Device && !IsDisconnected;
        DeviceKind = node.DeviceKind;
        KindByUser = node.KindByUser;
        isHub = node.Kind == UsbNodeKind.Hub;
        isController = node.Kind == UsbNodeKind.Controller;
        // An unplugged device is no display now.
        IsDisplay = node.IsDisplayLink && !IsDisconnected;
        var marked = PortId == null ? null : marks.GetValueOrDefault(PortId) ?? (pairedPortId == null ? null : marks.GetValueOrDefault(pairedPortId));
        if (SetField(ref cableMark, marked, nameof(Mark)))
        {
            OnPropertyChanged(nameof(MarkIndex));
            OnPropertyChanged(nameof(IsUserMarked));
            OnPropertyChanged(nameof(MarkBadge));
        }
        Status = node.Kind is UsbNodeKind.Hub or UsbNodeKind.Device
            ? Worst([report.Connections.TryGetValue(node.Id, out var value) ? value : null,
                half != null && report.Connections.TryGetValue(half.Hub.Id, out var other) ? other : null])
            : null;
        SetCounts(node.Kind == UsbNodeKind.Controller ? TreeCounts([node], marks) : null);
        var depth = report.Depths.GetValueOrDefault(node.Id);
        if (half != null && report.Depths.GetValueOrDefault(half.Hub.Id) is { } halfDepth && (depth == null || halfDepth.Tier > depth.Tier))
        {
            depth = halfDepth;
        }
        DepthText = !IsDisconnected && node.Kind is UsbNodeKind.Hub or UsbNodeKind.Device && depth != null ? DepthLine(depth) : null;
        PortType = node.Kind == UsbNodeKind.Port && node.UsbC == true ? Strings.UsbCPort : null;

        var wanted = node.Children
            .Where(child => child.Kind != UsbNodeKind.Port || Shown(child, report))
            .Select(child => (Node: child, Disconnected: false))
            .ToList();
        if (node.Kind == UsbNodeKind.Port && wanted.Count == 0)
        {
            // A disconnected device is shown at its last port, without what was behind it.
            wanted.AddRange(report.Missing
                .Where(missing => missing.PortId == node.Id)
                .Select(missing => (missing.Node with { Children = [] }, true)));
        }
        var monitors = context.Linked.GetValueOrDefault(node.Id) ?? [];
        UpdateChildren(wanted, report, marks, context, node.Kind == UsbNodeKind.Port ? node.Id : null,
            Children.Count(child => child.IsMonitorItem), monitors.Count > 0);
        UpdateMonitors(monitors, wanted.Count, report);
        if (node.Kind == UsbNodeKind.Port)
        {
            // A port with something on it folds into the rows of what is on it (#264): a connected hub or device, or the
            // disconnected devices kept at it. A port with a problem keeps its own row, with them below it.
            isFolded = node.Problem == null && Children.Count > 0;
            PortBadge = PortType;
            // #370: a half of a computer port shown as a row is no row, and what is on it needs no port box: the row is the port.
            isHalf = isFolded && context.Layout.PortHalves.Contains(node.Id);
            var boxed = isFolded && !isHalf;
            foreach (var child in Children)
            {
                child.HoverPortId = boxed ? node.Id : null;
                child.PortNumber = boxed ? node.PortNumber?.ToString(CultureInfo.CurrentCulture) ?? node.Name : null;
                child.PortName = boxed ? node.Name : null;
                child.PortBadge = boxed ? PortType : null;
            }
        }
        UpdateRows();
        PathStatus = Worst(Children.Select(child => child.PathStatus).Prepend(Status));
        if (SetField(ref displayBelow, Children.Any(child => child.IsDisplay || child.displayBelow), nameof(DisplayNote)))
        {
            OnPropertyChanged(nameof(HasDisplayBelow));
        }
    }

    /// <summary>
    /// "Tier 4, 2 hubs in front" (#307): the hubs in front are information only, and are left out when there are none.
    /// </summary>
    internal static string DepthLine(ConnectionDepth depth) => depth.HubsInFront switch
    {
        0 => string.Format(CultureInfo.CurrentCulture, Strings.NodeDepthTip, depth.Tier),
        1 => string.Format(CultureInfo.CurrentCulture, Strings.NodeDepthTipOneHub, depth.Tier),
        _ => string.Format(CultureInfo.CurrentCulture, Strings.NodeDepthTipHubs, depth.Tier, depth.HubsInFront),
    };

    private void UpdateChildren(
        IReadOnlyList<(UsbNode Node, bool Disconnected)> wanted, DiagnosticReport report, IReadOnlyDictionary<string, CableMark> marks,
        Context context, string? portId, int trailing, bool followed)
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
                item.Update(child, report, marks, context, childIsLast);
                Children.Insert(i, item);
                continue;
            }
            var at = Children.IndexOf(existing);
            if (at != i)
            {
                Children.Move(at, i);
            }
            existing.Update(child, report, marks, context, childIsLast);
        }
        while (Children.Count > wanted.Count + trailing)
        {
            Children.RemoveAt(wanted.Count);
        }
    }

    // "Hubs: 4 (3+1), devices: 7, deepest tier: 4" (#217, #298, #307): the sum of the hubs on each bus and, in brackets, per bus in a
    // fixed order, USB 2, USB 3 (#195), then any USB4 routers (#359). The deepest tier is of any bus. Without any known bus there are no brackets. "At least" when a hub, port or bus could not be read; a hub on a port whose bus is unknown
    // is in the total but on neither bus. Null when nothing is connected.
    private static CountsText? TreeCounts(IReadOnlyList<UsbNode> tree, IReadOnlyDictionary<string, CableMark> marks)
    {
        if (Count(tree, marks) is not { } counted)
        {
            return null;
        }
        var (all, perBus, devices, atLeast) = counted;
        var hubs = string.Format(CultureInfo.CurrentCulture, atLeast ? Strings.CountsHubsAtLeast : Strings.CountsHubs, all.Hubs);
        var depth = string.Format(CultureInfo.CurrentCulture, atLeast ? Strings.CountsDevicesAtLeast : Strings.CountsDevices, devices)
            + string.Format(CultureInfo.CurrentCulture, atLeast ? Strings.CountsDepthAtLeast : Strings.CountsDepth, all.DeepestTier);
        if (!perBus.AnyBusKnown)
        {
            return new CountsText(hubs, HubsText(counted), RoutersText(perBus), null, null, depth);
        }
        string Number(BusCounts bus) => bus.Hubs.ToString(CultureInfo.CurrentCulture);
        return new CountsText(hubs, HubsText(counted), RoutersText(perBus), Number(perBus.Usb2), Number(perBus.Usb3), depth);
    }

    /// <summary>
    /// "Hubs: 3 (2+1)" (#298, #359): the sum of the hubs on each bus, then in brackets those on USB 2 and USB 3, then any USB4 routers;
    /// "Hubs: 3" when no bus is known; "at least" as in <see cref="CountedTree.AtLeast"/>.
    /// </summary>
    internal static string HubsText(CountedTree counted) =>
        string.Format(CultureInfo.CurrentCulture, counted.AtLeast ? Strings.CountsHubsAtLeast : Strings.CountsHubs, HubsNumbers(counted))
        + RoutersText(counted.PerBus);

    /// <summary>" · USB4 routers: 1" when a controller reports a USB4 router (#359); empty otherwise.</summary>
    internal static string RoutersText(PerBusCounts perBus) => perBus.Usb4Routers is > 0 and var routers
        ? string.Format(CultureInfo.CurrentCulture, Strings.CountsRouters, routers)
        : "";

    /// <summary>The numbers of <see cref="HubsText"/> without the label and without "at least": "3 (2+1)" or "3".</summary>
    internal static string HubsNumbers(CountedTree counted) => counted.PerBus.AnyBusKnown
        ? string.Format(CultureInfo.CurrentCulture, Strings.CountsHubsPerBus, counted.All.Hubs,
            counted.PerBus.Usb2.Hubs, counted.PerBus.Usb3.Hubs)
        : counted.All.Hubs.ToString(CultureInfo.CurrentCulture);

    /// <summary>The counts of a tree, as the row counter and the side panel's table (#265) show them.</summary>
    /// <param name="All">All buses together: the sum of the hubs on each bus (#298), and the deepest tier of any bus.</param>
    /// <param name="PerBus">The same per bus; a hub or device on a port whose bus is not reported is on neither.</param>
    /// <param name="Devices">The connected devices, hubs not counted (<see cref="TopologyCounts.DeviceCount"/>, #307).</param>
    /// <param name="AtLeast">
    /// A hub, port or controller could not be read, or a counted hub or device is on a port whose bus is not reported
    /// while another bus is: the numbers are minimums.
    /// </param>
    internal sealed record CountedTree(BusCounts All, PerBusCounts PerBus, int Devices, bool AtLeast);

    /// <summary>
    /// Counts <paramref name="tree"/> for the row counter and the side panel's table (#265), so both follow one rule;
    /// the user's marks in <paramref name="marks"/> count in the tiers and hubs. Null when nothing is connected.
    /// </summary>
    internal static CountedTree? Count(IReadOnlyList<UsbNode> tree, IReadOnlyDictionary<string, CableMark> marks)
    {
        var depths = TopologyCounts.Depths(tree, marks).Values;
        var perBus = TopologyCounts.PerBus(tree, marks);
        var atLeast = TopologyCounts.Unreadable(tree).Any() || (perBus.AnyBusKnown && perBus.BusUnknown);
        if (!depths.Any() && !atLeast)
        {
            return null;
        }
        return new CountedTree(
            new BusCounts(perBus.Hubs, depths.Select(depth => depth.Tier).DefaultIfEmpty(0).Max()),
            perBus, TopologyCounts.DeviceCount(tree), atLeast);
    }

    /// <summary>
    /// The counts in pieces, so the window can show "(3+1)" with the hubs of each bus in bold; hovering shows the text
    /// and which bus is which. The text (tree as text, report) is the same: "Hubs: 4 (3+1), ..." (#298).
    /// </summary>
    /// <param name="Hubs">The hubs label with the total only: "Hubs: 4".</param>
    /// <param name="HubsText">The same with the hubs per bus and any routers: "Hubs: 4 (3+1) · USB4 routers: 1".</param>
    /// <param name="Routers">The routers part, " · USB4 routers: 1", or empty (#359).</param>
    /// <param name="Depth">What follows the hubs: ", devices: 7, deepest tier: 4".</param>
    private sealed record CountsText(string Hubs, string HubsText, string Routers, string? Usb2, string? Usb3, string Depth)
    {
        public string Head => Usb2 == null ? Hubs : Hubs + " (";

        public string Tail => (Usb2 == null ? "" : ")") + Routers + Depth;

        public string Tip => Usb2 == null ? ToString() : ToString() + Environment.NewLine + Strings.CountsPerBusTip;

        public override string ToString() => HubsText + Depth;
    }

    // The name, with the vendor and product id and the bus of its port (#370) in brackets when known: "WPP20 (2A28:0020, USB 2)".
    // A USB4 router has its own row below its port (#359, #370), so a controller is named alone.
    private static string Describe(UsbNode node, bool isDisconnected, string? bus = null)
    {
        var name = (node.VendorId, node.ProductId, bus) switch
        {
            ({ } vendor, { } product, null) => string.Format(CultureInfo.CurrentCulture, Strings.NodeWithIds, node.Name, vendor, product),
            ({ } vendor, { } product, { } known) => string.Format(CultureInfo.CurrentCulture, Strings.NodeWithIdsAndBus, node.Name, vendor, product, known),
            (_, _, { } known) => string.Format(CultureInfo.CurrentCulture, Strings.NodeWithBus, node.Name, known),
            _ => node.Name,
        };
        if (isDisconnected)
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.NodeDisconnected, name);
        }
        return node.Problem == null ? name : string.Format(CultureInfo.CurrentCulture, Strings.NodeProblem, name, node.Problem);
    }
}
