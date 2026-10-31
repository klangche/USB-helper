using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using UsbHelper.App.Resources;
using UsbHelper.Core.Database;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;

namespace UsbHelper.App.ViewModels;

/// <summary>
/// A platform or computer profile in one of the checkbox lists. Unchecked at start, except the platform profile of the
/// detected operating system and processor.
/// </summary>
public sealed class ProfileOption(string label, ResolvedProfile profile) : ObservableObject
{
    private bool isChecked;

    public string Label { get; } = label;

    public ResolvedProfile Profile { get; } = profile;

    public bool IsChecked
    {
        get => isChecked;
        set => SetField(ref isChecked, value);
    }
}

/// <summary>One checked profile in the status field.</summary>
/// <param name="Word">Its result: stable, potentially unstable, unstable or unknown.</param>
/// <param name="Details">Its hub, tier, hop and display checks.</param>
public sealed record StatusRow(string Label, string Word, string Color, IReadOnlyList<string> Details);

/// <param name="Header">Profile (when only some checked profiles report it), kind (detected, database, risk, unknown) and subject.</param>
public sealed record FindingItem(string Header, string Message);

public sealed record DisconnectedItem(string Text, RelayCommand Clear);

/// <summary>
/// The main window's state. Uses only Core types, so it is the same on every operating system.
/// Call <see cref="Load"/> once, then <see cref="Apply"/> for every batch of changes, on the UI thread.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly SessionHistory history = new();
    private readonly Func<int?> readExternalDisplays;
    private readonly ComputerIdentity identity;
    // The user's marks for active cables and extenders: extra hops per port id. They last while the app runs.
    private readonly Dictionary<string, int> extraHops = [];
    private IReadOnlyList<UsbNode> tree = [];
    private NodeViewModel? selectedNode;
    private string? notice;
    private bool isLoading = true;
    private SystemsReport report;

    /// <param name="readExternalDisplays">Reads the active external display count, or null when unknown. Called on every refresh.</param>
    public MainViewModel(
        HardwareDatabase? database, IReadOnlyList<DatabaseError> databaseErrors, ComputerIdentity identity, Func<int?>? readExternalDisplays = null)
    {
        this.readExternalDisplays = readExternalDisplays ?? (() => null);
        this.identity = identity;
        DatabaseErrors = databaseErrors.Select(error => error.ToString()).ToList();
        // Only the detected operating system's platform profile is checked at start; computer profiles, even an exact
        // match, are checked by the user.
        OperatingSystems = Options((database?.Platforms.Values ?? []).Select(profile =>
            new ProfileOption(profile.Id, profile) { IsChecked = profile.Id == identity.Platform }));
        Computers = Options((database?.Computers.Values ?? []).Select(profile =>
            new ProfileOption(ComputerLabel(profile), profile)));
        HostName = HostNameFor(database, identity);
        DetectedComputer = string.Format(CultureInfo.CurrentCulture, Strings.DetectedComputer,
            identity.Manufacturer ?? Strings.UnknownValue, identity.Model ?? Strings.UnknownValue);
        report = DiagnosticEvaluator.EvaluateAll(tree, [], []);
    }

    private static string ComputerLabel(ResolvedProfile profile)
    {
        var reported = $"{profile.Find("manufacturer")?.Scalar} {profile.Find("model")?.Scalar}";
        return profile.Find("name")?.Scalar is string name ? $"{name} ({reported})" : $"{reported} ({profile.Id})";
    }

    // The matching computer profile's readable name, else manufacturer and model as reported, else the machine name.
    private static string HostNameFor(HardwareDatabase? database, ComputerIdentity identity)
    {
        var reported = string.Join(' ', new[] { identity.Manufacturer, identity.Model }.Where(part => !string.IsNullOrWhiteSpace(part)));
        if (database != null && !string.IsNullOrWhiteSpace(identity.Manufacturer) && !string.IsNullOrWhiteSpace(identity.Model)
            && database.Computers.TryGetValue(ComputerProfileId.From(identity.Manufacturer, identity.Model), out var profile)
            && profile.Find("name")?.Scalar is string name)
        {
            return name;
        }
        return reported.Length > 0 ? reported : identity.MachineName ?? Strings.ThisComputer;
    }

    /// <summary>The name of the tree's top item, this computer.</summary>
    public string HostName { get; }

    /// <summary>Platform profiles: generic limits, for computers without a profile of their own.</summary>
    public IReadOnlyList<ProfileOption> OperatingSystems { get; }

    public IReadOnlyList<ProfileOption> Computers { get; }

    private IReadOnlyList<ProfileOption> Options(IEnumerable<ProfileOption> options)
    {
        var sorted = options.OrderBy(option => option.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
        foreach (var option in sorted)
        {
            option.PropertyChanged += (_, _) => Refresh();
        }
        return sorted;
    }

    private IReadOnlyList<ProfileOption> Checked => [.. OperatingSystems.Where(option => option.IsChecked), .. Computers.Where(option => option.IsChecked)];

    public bool NothingChecked => StatusRows.Count == 0;

    public string DetectedComputer { get; }

    /// <summary>A profile can only be exported when the computer reports manufacturer and model: its id comes from them.</summary>
    public bool CanExport => !IsLoading && !string.IsNullOrWhiteSpace(identity.Manufacturer) && !string.IsNullOrWhiteSpace(identity.Model);

    public string ExportFileName => ComputerProfileExport.FileName(identity);

    /// <summary>This computer as a computer profile YAML, to send to the developers.</summary>
    public string ExportProfile() =>
        ComputerProfileExport.ToYaml(identity, tree, readExternalDisplays(), AppVersion(), DateOnly.FromDateTime(DateTime.Now));

    // The release build sets the version; "+commit" metadata is left out.
    private static string AppVersion() =>
        typeof(MainViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "unknown";

    public IReadOnlyList<string> DatabaseErrors { get; }

    public bool HasDatabaseErrors => DatabaseErrors.Count > 0;

    /// <summary>True until the first USB tree arrives; the window is shown before detection has run.</summary>
    public bool IsLoading
    {
        get => isLoading;
        set
        {
            if (SetField(ref isLoading, value))
            {
                OnPropertyChanged(nameof(CanExport)); // Not before the USB tree is known.
            }
        }
    }

    /// <summary>A message shown instead of the tree, for example when detection is not available.</summary>
    public string? Notice
    {
        get => notice;
        set => SetField(ref notice, value);
    }

    /// <summary>The tree: one item, the host, with the controllers as its children. Empty until the first refresh.</summary>
    public ObservableCollection<NodeViewModel> Roots { get; } = [];

    public NodeViewModel? SelectedNode
    {
        get => selectedNode;
        set
        {
            if (SetField(ref selectedNode, value))
            {
                OnPropertyChanged(nameof(CanMark));
                OnPropertyChanged(nameof(SelectedExtraHops));
            }
        }
    }

    /// <summary>True when the selected item is a port, which can be marked with extra hops.</summary>
    public bool CanMark => SelectedNode?.IsPort == true;

    /// <summary>The choices for a mark, by number of extra hops: none, then +1 to +5.</summary>
    public IReadOnlyList<string> MarkChoices { get; } =
        [Strings.MarkNone, .. Enumerable.Range(1, 5).Select(hops => string.Format(CultureInfo.CurrentCulture, Strings.MarkHops, hops))];

    /// <summary>
    /// Extra hops for an active cable or extender the operating system does not show, on the selected port. They count
    /// in every tier and hop check of everything behind the port. 0 removes the mark.
    /// </summary>
    public int SelectedExtraHops
    {
        get => CanMark ? extraHops.GetValueOrDefault(SelectedNode!.Id) : 0;
        set
        {
            if (!CanMark || value == SelectedExtraHops || value < 0)
            {
                return;
            }
            if (value == 0)
            {
                extraHops.Remove(SelectedNode!.Id);
            }
            else
            {
                extraHops[SelectedNode!.Id] = value;
            }
            OnPropertyChanged(nameof(SelectedExtraHops));
            Refresh();
        }
    }

    public string OverallText => string.Format(CultureInfo.CurrentCulture, Strings.Overall, StatusColors.Text(report.Overall));

    public string OverallColor => StatusColors.Hex(report.Overall);

    /// <summary>One row per checked profile: operating systems first, then computers.</summary>
    public ObservableCollection<StatusRow> StatusRows { get; } = [];

    /// <summary>Devices that are gone, then devices that disconnected and came back; each can be cleared.</summary>
    public ObservableCollection<DisconnectedItem> Disconnected { get; } = [];

    public ObservableCollection<FindingItem> Findings { get; } = [];

    public void Load(IReadOnlyList<UsbNode> detected)
    {
        IsLoading = false;
        tree = detected;
        history.Record(tree);
        Refresh();
    }

    public void Apply(IReadOnlyList<UsbChange> changes)
    {
        tree = UsbTree.Apply(tree, changes);
        history.Record(tree);
        foreach (var change in changes.Where(change => change.Kind == UsbChangeKind.Reconnected))
        {
            foreach (var node in change.Node.Children)
            {
                history.Reconnect(node.Id);
            }
        }
        Refresh();
    }

    private void Refresh()
    {
        var options = Checked;
        var reconnected = history.Reconnected(tree);
        report = DiagnosticEvaluator.EvaluateAll(tree, [.. options.Select(option => option.Profile)], history.Missing(tree), readExternalDisplays(),
            [.. reconnected.Select(device => device.Node.Id)], extraHops);
        // The tree needs only each line's worst status and the missing devices, which are the same for every profile.
        var lines = report.Counts with { Connections = report.Connections };

        if (Roots.Count == 0)
        {
            Roots.Add(NodeViewModel.Host(HostName, tree, lines, extraHops));
        }
        else
        {
            Roots[0].UpdateHost(HostName, tree, lines, extraHops);
        }

        OnPropertyChanged(nameof(OverallText));
        OnPropertyChanged(nameof(OverallColor));

        StatusRows.Clear();
        for (var i = 0; i < options.Count; i++)
        {
            var result = report.Profiles[i].Report;
            StatusRows.Add(new StatusRow(options[i].Label, StatusColors.Word(result.Overall), StatusColors.Hex(result.Overall),
                [
                    LimitText(Strings.LimitHubs, result.Hubs),
                    LimitText(Strings.LimitTiers, result.Tiers),
                    LimitText(Strings.LimitHops, result.Hops),
                    DisplaysText(result.Displays),
                ]));
        }
        OnPropertyChanged(nameof(NothingChecked));

        Disconnected.Clear();
        foreach (var missing in report.Counts.Missing)
        {
            Disconnected.Add(DisconnectedItem(Strings.DisconnectedFrom, missing));
        }
        foreach (var device in reconnected)
        {
            Disconnected.Add(DisconnectedItem(Strings.ReconnectedOn, device));
        }
        Findings.Clear();
        if (options.Count == 0)
        {
            foreach (var finding in report.Counts.Findings)
            {
                Findings.Add(new FindingItem(FindingHeader(finding), finding.Message));
            }
            return;
        }
        // A finding every checked profile reports (an operating system error, for example) is listed once; the others
        // name their profile.
        var shared = report.Profiles
            .Select(result => (IEnumerable<Finding>)result.Report.Findings)
            .Aggregate((all, findings) => all.Intersect(findings))
            .ToHashSet();
        foreach (var finding in shared)
        {
            Findings.Add(new FindingItem(FindingHeader(finding), finding.Message));
        }
        for (var i = 0; i < options.Count; i++)
        {
            foreach (var finding in report.Profiles[i].Report.Findings.Where(finding => !shared.Contains(finding)))
            {
                Findings.Add(new FindingItem(
                    string.Format(CultureInfo.CurrentCulture, Strings.FindingHeaderForProfile, options[i].Label, KindText(finding.Kind), finding.SubjectName),
                    finding.Message));
            }
        }
    }

    private DisconnectedItem DisconnectedItem(string format, MissingDevice device)
    {
        var id = device.Node.Id;
        return new DisconnectedItem(
            string.Format(CultureInfo.CurrentCulture, format, device.Node.Name, device.PortName),
            new RelayCommand(() =>
            {
                history.Clear(id);
                Refresh();
            }));
    }

    private static string FindingHeader(Finding finding) =>
        string.Format(CultureInfo.CurrentCulture, Strings.FindingHeader, KindText(finding.Kind), finding.SubjectName);

    private static string DisplaysText(DisplayCheck displays) =>
        string.Format(CultureInfo.CurrentCulture, Strings.LimitLine, Strings.LimitDisplays,
            displays.ExternalDisplays?.ToString(CultureInfo.CurrentCulture) ?? Strings.UnknownValue,
            displays.MaxExternal?.ToString(CultureInfo.CurrentCulture) ?? Strings.UnknownValue,
            StateText(displays.State),
            displays.Verified ? Strings.Verified : Strings.Unverified);

    private static string LimitText(string label, LimitCheck limit) =>
        string.Format(CultureInfo.CurrentCulture, Strings.LimitLine, label, limit.Actual,
            limit.Limit?.ToString(CultureInfo.CurrentCulture) ?? Strings.UnknownValue,
            StateText(limit.State),
            limit.Verified ? Strings.Verified : Strings.Unverified);

    private static string StateText(LimitState state) => state switch
    {
        LimitState.Within => Strings.LimitWithin,
        LimitState.Exceeded => Strings.LimitExceeded,
        _ => Strings.LimitUnknown,
    };

    private static string KindText(FactKind kind) => kind switch
    {
        FactKind.DetectedFact => Strings.KindDetectedFact,
        FactKind.VerifiedDatabaseFact => Strings.KindVerifiedDatabaseFact,
        FactKind.PredictedRisk => Strings.KindPredictedRisk,
        _ => Strings.KindUnknown,
    };
}
