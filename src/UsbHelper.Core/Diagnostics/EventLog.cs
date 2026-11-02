using System.Globalization;
using System.Text;
using UsbHelper.Core.Database;
using UsbHelper.Core.Detection;

namespace UsbHelper.Core.Diagnostics;

/// <summary>One logged event: when it happened and a short English text.</summary>
public sealed record LogEntry(DateTimeOffset Time, string Text)
{
    /// <summary>The entry as one line of the log export, with local date and time.</summary>
    public override string ToString() => $"{Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}  {Text}";
}

/// <summary>
/// Every event of this session, oldest first (owner decision in #85): start and loading stages, the database load
/// result, connects, disconnects, reconnects, operating system errors, profile check changes, cable marks and every
/// finding, the unknown ones included. Kept in memory only; <see cref="Export"/> writes it as text.
/// </summary>
public sealed class EventLog(TimeProvider? clock = null)
{
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private readonly List<LogEntry> entries = [];
    // The findings of the last call to Findings, as their log texts: a finding is logged when it appears.
    private HashSet<string> findings = [];

    public IReadOnlyList<LogEntry> Entries => entries;

    /// <summary>Raised on the caller's thread for every new entry.</summary>
    public event Action<LogEntry>? Added;

    public void Add(string text)
    {
        var entry = new LogEntry(clock.GetLocalNow(), text);
        entries.Add(entry);
        Added?.Invoke(entry);
    }

    public void DatabaseLoaded(HardwareDatabase database) =>
        Add($"Database loaded: {database.Platforms.Count} platform profiles, {database.Computers.Count} computer profiles");

    public void DatabaseFailed(IReadOnlyList<DatabaseError> errors)
    {
        Add($"Database not loaded: {errors.Count} errors");
        foreach (var error in errors)
        {
            Add($"Database error: {error}");
        }
    }

    /// <summary>The first full read of the USB tree, with every error the operating system reports in it.</summary>
    public void TreeRead(IReadOnlyList<UsbNode> tree)
    {
        Add($"USB devices read: {tree.Count} controllers");
        foreach (var controller in tree)
        {
            foreach (var (text, problem) in Problems(controller, location: null))
            {
                Add($"Error on {text}: {problem}");
            }
        }
    }

    /// <summary>
    /// Logs what <paramref name="changes"/> connect, disconnect and reconnect, and every operating system error that
    /// is new. Call before applying them to <paramref name="tree"/>.
    /// </summary>
    public void Changes(IReadOnlyList<UsbNode> tree, IReadOnlyList<UsbChange> changes)
    {
        foreach (var change in changes)
        {
            var hub = change.ParentId == null ? null : UsbTree.Find(tree, change.ParentId)?.Name;
            if (change.Kind == UsbChangeKind.Reconnected)
            {
                foreach (var node in change.Node.Children)
                {
                    Add($"Reconnected: {node.Name} on {change.Node.Name} of {hub}");
                }
                continue;
            }
            var before = change.ParentId == null && change.Kind == UsbChangeKind.Removed
                ? change.Node
                : UsbTree.Find(tree, change.Node.Id);
            var after = change.Kind == UsbChangeKind.Removed && change.ParentId == null ? null : change.Node;
            var location = hub == null ? null : $"{change.Node.Name} of {hub}";
            var old = before == null ? [] : Attached(before, location).ToList();
            var now = after == null ? [] : Attached(after, location).ToList();
            if (change.ParentId == null && change.Kind == UsbChangeKind.Added)
            {
                Add($"Connected: controller {change.Node.Name}");
            }
            if (after == null)
            {
                Add($"Disconnected: controller {change.Node.Name}");
            }
            foreach (var (node, port) in old.Where(item => now.All(other => other.Node.Id != item.Node.Id)))
            {
                Add($"Disconnected: {node.Name} from {port}");
            }
            foreach (var (node, port) in now.Where(item => old.All(other => other.Node.Id != item.Node.Id)))
            {
                Add($"Connected: {node.Name} on {port}");
            }
            if (after != null)
            {
                var known = before == null ? [] : Problems(before, location).ToHashSet();
                foreach (var (text, problem) in Problems(after, location).Where(item => !known.Contains(item)))
                {
                    Add($"Error on {text}: {problem}");
                }
            }
        }
    }

    public void ProfileChecked(string label, bool isChecked) => Add($"Profile {(isChecked ? "checked" : "unchecked")}: {label}");

    /// <param name="name">The hub or device the marked line leads to.</param>
    /// <param name="hops">The extra hops; 0 when the mark was removed.</param>
    public void CableMark(string name, int hops) =>
        Add(hops == 0 ? $"Cable mark removed from the line to {name}" : $"Cable mark: +{hops} hops on the line to {name}");

    /// <summary>
    /// The current findings with the profile each belongs to (null: none checked). Each is logged when it appears; one
    /// that goes away and comes back is logged again.
    /// </summary>
    public void Findings(IEnumerable<(string? Profile, Finding Finding)> current)
    {
        var texts = current.Select(item =>
            $"Finding ({item.Profile ?? "no profile"}, {KindText(item.Finding.Kind)}): {item.Finding.SubjectName}: {item.Finding.Message}").ToList();
        foreach (var text in texts.Distinct().Where(text => !findings.Contains(text)))
        {
            Add(text);
        }
        findings = [.. texts];
    }

    /// <summary>The whole log as plain text, one line per entry.</summary>
    public string Export()
    {
        var text = new StringBuilder();
        foreach (var entry in entries)
        {
            text.AppendLine(entry.ToString());
        }
        return text.ToString();
    }

    private static string KindText(FactKind kind) => kind switch
    {
        FactKind.DetectedFact => "detected",
        FactKind.VerifiedDatabaseFact => "database",
        FactKind.PredictedRisk => "risk",
        _ => "unknown",
    };

    // Every hub and device on a port in or below node, with "Port N of Hub". location is node's own when it is a port.
    private static IEnumerable<(UsbNode Node, string Port)> Attached(UsbNode node, string? location)
    {
        foreach (var (child, port) in Below(node, location))
        {
            if (child.Kind is UsbNodeKind.Hub or UsbNodeKind.Device && port != null)
            {
                yield return (child, port);
            }
        }
    }

    // Every element with a problem in or below node: hubs and devices by name, ports as "Port N of Hub".
    private static IEnumerable<(string Text, string Problem)> Problems(UsbNode node, string? location) =>
        Below(node, location).Where(item => item.Node.Problem != null)
            .Select(item => (item.Node.Kind == UsbNodeKind.Port ? item.Port ?? item.Node.Name : item.Node.Name, item.Node.Problem!));

    // node and everything below it. For a hub or device: the port it is on ("Port N of Hub"); for a port: itself.
    private static IEnumerable<(UsbNode Node, string? Port)> Below(UsbNode node, string? location)
    {
        yield return (node, location);
        foreach (var child in node.Children)
        {
            var childLocation = node.Kind switch
            {
                UsbNodeKind.Hub => $"{child.Name} of {node.Name}",
                UsbNodeKind.Port => location,
                _ => null,
            };
            foreach (var item in Below(child, childLocation))
            {
                yield return item;
            }
        }
    }
}
