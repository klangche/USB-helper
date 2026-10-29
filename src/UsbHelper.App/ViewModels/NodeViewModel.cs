using System.Collections.ObjectModel;
using System.Globalization;
using UsbHelper.App.Resources;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;

namespace UsbHelper.App.ViewModels;

/// <summary>
/// One item in the tree view. <see cref="Update"/> changes it in place and keeps child items whose id is unchanged,
/// so expansion and selection survive updates and only changed items notify the view.
/// </summary>
public sealed class NodeViewModel : ObservableObject
{
    private string text = "";
    private ConnectionStatus? status;
    private bool isExpanded = true;

    private NodeViewModel(string id, UsbNodeKind kind, bool isDisconnected)
    {
        Id = id;
        Kind = kind;
        IsDisconnected = isDisconnected;
    }

    public string Id { get; }

    public UsbNodeKind Kind { get; }

    /// <summary>A device seen earlier, shown at its last port.</summary>
    public bool IsDisconnected { get; }

    public ObservableCollection<NodeViewModel> Children { get; } = [];

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
            }
        }
    }

    public string LineColor => Status is { } value ? StatusColors.Hex(value) : StatusColors.None;

    public string? StatusText => Status is { } value ? StatusColors.Text(value) : null;

    public bool IsExpanded
    {
        get => isExpanded;
        set => SetField(ref isExpanded, value);
    }

    public static NodeViewModel Create(UsbNode node, DiagnosticReport report) =>
        Create(node, report, isDisconnected: false);

    /// <summary>Brings this item and its children up to date with <paramref name="node"/> and <paramref name="report"/>.</summary>
    public void Update(UsbNode node, DiagnosticReport report)
    {
        Text = Describe(node, IsDisconnected);
        Status = node.Kind is UsbNodeKind.Hub or UsbNodeKind.Device && report.Connections.TryGetValue(node.Id, out var value)
            ? value
            : null;

        var wanted = node.Children.Select(child => (Node: child, Disconnected: false)).ToList();
        if (node.Kind == UsbNodeKind.Port && wanted.Count == 0)
        {
            // A disconnected device is shown at its last port, without what was behind it.
            wanted.AddRange(report.Missing
                .Where(missing => missing.PortId == node.Id)
                .Select(missing => (missing.Node with { Children = [] }, true)));
        }
        for (var i = 0; i < wanted.Count; i++)
        {
            var (child, disconnected) = wanted[i];
            var existing = Children.FirstOrDefault(item => item.Id == child.Id && item.IsDisconnected == disconnected);
            if (existing == null)
            {
                Children.Insert(i, Create(child, report, disconnected));
                continue;
            }
            var at = Children.IndexOf(existing);
            if (at != i)
            {
                Children.Move(at, i);
            }
            existing.Update(child, report);
        }
        while (Children.Count > wanted.Count)
        {
            Children.RemoveAt(Children.Count - 1);
        }
    }

    private static NodeViewModel Create(UsbNode node, DiagnosticReport report, bool isDisconnected)
    {
        var item = new NodeViewModel(node.Id, node.Kind, isDisconnected);
        item.Update(node, report);
        return item;
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
