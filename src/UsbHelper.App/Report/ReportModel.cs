using UsbHelper.App.Resources;
using UsbHelper.App.ViewModels;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;

namespace UsbHelper.App.Report;

/// <summary>
/// Everything the PDF setup report (#231) shows, as plain data, so the renderer needs no view and no database.
/// <see cref="MainViewModel.Report"/> builds it from the current setup. No serial numbers and no computer name, the same
/// rule as the setup export.
/// </summary>
/// <param name="CompanyName">
/// Shown in the header and footer only when no logo is shown (#232), and always in the metadata; null when none is set.
/// </param>
/// <param name="LogoPath">An .svg file (drawn as vector) or a PNG or WebP file; null when there is no logo.</param>
/// <param name="RoomName">The room or system name the user typed; null when none is entered.</param>
/// <param name="Overall">The worst result of all checked profiles over all controllers.</param>
/// <param name="Profiles">Each checked profile's result, or one entry without a profile when none is checked.</param>
/// <param name="Displays">The active monitors; null when they could not be read.</param>
/// <param name="Tree">The USB tree as the window shows it, top down.</param>
public sealed record ReportModel(
    string? CompanyName,
    string? LogoPath,
    string? RoomName,
    string AppVersion,
    DateTime Created,
    ComputerIdentity Computer,
    ConnectionStatus Overall,
    ReportCounts Counts,
    IReadOnlyList<ReportProfile> Profiles,
    IReadOnlyList<ReportDisplay>? Displays,
    IReadOnlyList<ReportRow> Tree,
    IReadOnlyList<ReportFinding> Findings)
{
    /// <summary>The email the report features are unlocked for (#232), in the header and footer; null when none.</summary>
    public string? LicensedTo { get; init; }

    /// <summary>The database in use (#389), "Database v4 · 2026-11-24 (abc1234)", under the header; null when unknown.</summary>
    public string? Database { get; init; }

    /// <summary>The philosophy with its file and sign-off (#389), under the header; null when none is used.</summary>
    public string? Philosophy { get; init; }

    /// <summary>
    /// The database files of the checked profiles, as the findings name them: the report writes "Hardware database" in
    /// their place (#247).
    /// </summary>
    public IReadOnlyList<string> DatabaseFiles { get; init; } = [];

    /// <summary>The results grouped by outcome (#369), as lines: a group's title and systems, then its issues indented.</summary>
    public IReadOnlyList<string> Outcomes { get; init; } = [];

    /// <summary>"Ports tested" (#342): the summary of the ports the setup was tried on, or null when it was tried on none.</summary>
    public string? PortsTested { get; init; }
}

/// <summary>The setup's counts over all controllers.</summary>
/// <param name="Hubs">The sum of the hubs on each bus, those on an unknown bus included (#298).</param>
/// <param name="Usb2Hubs">Hubs on the USB2 bus; null when no bus is known. The same for USB3 and USB4.</param>
/// <param name="Devices">The connected USB devices, hubs not counted (#307).</param>
/// <param name="AtLeast">A hub could not be read, so the real counts may be higher.</param>
/// <param name="ExternalDisplays">Null when unknown.</param>
public sealed record ReportCounts(int Hubs, int? Usb2Hubs, int? Usb3Hubs, int? Usb4Routers, int Devices, int DeepestTier, bool AtLeast, int? ExternalDisplays);

/// <summary>One checked profile: its result and its hub, tier and display checks; no checks without a profile.</summary>
public sealed record ReportProfile(string Label, ConnectionStatus Overall, IReadOnlyList<ReportCheck> Checks);

/// <param name="Limit">Null when the profile does not know it, or when the check has none (disconnected devices).</param>
/// <param name="Measured">Null when unknown.</param>
/// <param name="Status">Green within the limit, grey when unknown, otherwise the problem's status as the app shows it.</param>
public sealed record ReportCheck(string Name, long? Limit, int? Measured, ConnectionStatus Status);

/// <param name="ConnectedTo">The USB hub or device the monitor is part of; null when it is linked to none.</param>
public sealed record ReportDisplay(string Monitor, string Output, string? ConnectedTo);

/// <summary>A finding; <see cref="Profile"/> names the checked profile when several are checked, never for a detected fact.</summary>
public sealed record ReportFinding(FactKind Kind, string? Profile, string Subject, string Message);

/// <summary>One row of the tree, with what <see cref="TreeText"/> writes for it.</summary>
/// <param name="Level">0 for the host; its children 1, and so on.</param>
/// <param name="Guides">One entry per ancestor column left of the elbow: true where that ancestor's trunk continues past this row.</param>
/// <param name="IsLast">The last child of its parent: its elbow ends the parent's trunk.</param>
/// <param name="Status">The line's status; null for controllers, root hubs, ports and groups.</param>
/// <param name="Badges">The cable mark ("+2 hubs", "+1 tier") and "Display", in that order, when they apply.</param>
/// <param name="Notes">The display note and the counts, when there are.</param>
public sealed record ReportRow(
    int Level,
    IReadOnlyList<bool> Guides,
    bool IsLast,
    string Text,
    ConnectionStatus? Status,
    IReadOnlyList<string> Badges,
    string? PortType,
    IReadOnlyList<string> Notes)
{
    /// <summary>The rows of <paramref name="root"/> and everything below it, collapsed items included.</summary>
    public static IReadOnlyList<ReportRow> Of(NodeViewModel root)
    {
        var rows = new List<ReportRow> { Row(root, 0, [], true) };
        Add(root, 1, [], rows);
        return rows;
    }

    private static void Add(NodeViewModel item, int level, IReadOnlyList<bool> guides, List<ReportRow> rows)
    {
        foreach (var (child, last) in item.WrittenChildren)
        {
            rows.Add(Row(child, level, guides, last));
            Add(child, level + 1, [.. guides, !last], rows);
        }
    }

    private static ReportRow Row(NodeViewModel item, int level, IReadOnlyList<bool> guides, bool isLast) => new(
        level,
        guides,
        isLast,
        item.Text,
        item.Status,
        [.. new[] { item.MarkBadge, item.IsDisplay ? Strings.DisplayBadge : null }.OfType<string>()],
        item.PortType,
        [.. new[] { item.DisplayNote, item.Counts }.OfType<string>().Concat(item.LimitBadges.Select(badge => badge.Text)).Concat(item.CableNoteLines)]);
}
