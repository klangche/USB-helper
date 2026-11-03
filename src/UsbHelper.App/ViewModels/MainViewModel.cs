using System.Collections.ObjectModel;
using System.Globalization;
using UsbHelper.App.Resources;
using UsbHelper.Core.Database;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;
using UsbHelper.Core.Topology;

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

/// <param name="Header">
/// Profile (the strictest, when several are checked; never for a detected fact), kind (detected, database, risk, unknown)
/// and subject.
/// </param>
public sealed record FindingItem(string Header, string Message);

public sealed record DisconnectedItem(string Text, RelayCommand Clear);

/// <summary>
/// The main window's state. Uses only Core types, so it is the same on every operating system.
/// The window shows before anything is read (#86): call <see cref="LoadDatabase"/>, <see cref="ReadingDisplays"/> and
/// <see cref="Load(IReadOnlyList{UsbNode}, int?)"/> once each, in that order, then <see cref="Apply"/> for every batch of
/// changes, on the UI thread.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly SessionHistory history = new();
    private readonly EventLog log;
    private readonly Func<int?> readExternalDisplays;
    private readonly ComputerIdentity identity;
    // The user's marks for active cables and extenders: extra hops per port id. They last while the app runs.
    private readonly Dictionary<string, int> extraHops = [];
    private IReadOnlyList<UsbNode> tree = [];
    private NodeViewModel? selectedNode;
    private string? notice;
    private bool isLoading = true;
    private string loadingStage = Strings.LoadingDatabase;
    // The result for the selected controller's tree, or for all controllers; shown in the status field and findings.
    private SystemsReport report;
    private string? selectedControllerId;
    // The host is selected: the status covers all controllers.
    private bool allControllers;
    private bool controllersShown;

    /// <summary>A model whose database is already loaded (or failed to load): it waits for the USB tree.</summary>
    public MainViewModel(
        HardwareDatabase? database, IReadOnlyList<DatabaseError> databaseErrors, ComputerIdentity identity, Func<int?>? readExternalDisplays = null,
        EventLog? log = null, SettingsViewModel? settings = null)
        : this(identity, readExternalDisplays, log, settings)
    {
        LoadDatabase(database, databaseErrors);
    }

    /// <summary>A model for the window before anything is read: it shows "Loading database..." first.</summary>
    /// <param name="readExternalDisplays">Reads the active external display count, or null when unknown. Called on every refresh.</param>
    /// <param name="log">The session's event log; a new one when null.</param>
    /// <param name="settings">The settings (#126); the defaults, kept in memory only, when null.</param>
    public MainViewModel(ComputerIdentity identity, Func<int?>? readExternalDisplays = null, EventLog? log = null, SettingsViewModel? settings = null)
    {
        Settings = settings ?? new SettingsViewModel();
        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.RememberCheckedProfiles))
            {
                KeepCheckedProfiles(); // Turned on: keep what is checked now.
            }
        };
        this.log = log ?? new EventLog();
        Log = new LogViewModel(this.log);
        this.readExternalDisplays = readExternalDisplays ?? (() => null);
        this.identity = identity;
        HostName = HostNameFor(null, identity);
        DetectedComputer = string.Format(CultureInfo.CurrentCulture, Strings.DetectedComputer,
            identity.Manufacturer ?? Strings.UnknownValue, identity.Model ?? Strings.UnknownValue);
        report = DiagnosticEvaluator.EvaluateAll(tree, [], []);
        ExportProfileCommand = new RelayCommand(() => Window?.ExportProfile(), () => CanExport);
        ExportLogCommand = new RelayCommand(() => Window?.ExportLog());
        ExitCommand = new RelayCommand(() => Window?.Exit());
        AboutCommand = new RelayCommand(() => Window?.ShowAbout());
        SettingsCommand = new RelayCommand(() => Window?.ShowSettings());
    }

    /// <summary>The database load result: the profiles fill in and the window moves on to "Reading USB devices...".</summary>
    public void LoadDatabase(HardwareDatabase? loaded, IReadOnlyList<DatabaseError> errors)
    {
        if (loaded != null)
        {
            log.DatabaseLoaded(loaded);
        }
        else if (errors.Count > 0)
        {
            log.DatabaseFailed(errors);
        }
        DatabaseErrors = errors.Select(error => error.ToString()).ToList();
        OnPropertyChanged(nameof(DatabaseErrors));
        OnPropertyChanged(nameof(HasDatabaseErrors));
        // Only the detected operating system's platform profile is checked at start; computer profiles, even an exact
        // match, are checked by the user. With "remember" on (#126), the profiles checked last time instead.
        var remembered = Settings.RememberCheckedProfiles && (Settings.CheckedOperatingSystems.Count > 0 || Settings.CheckedComputers.Count > 0);
        AddOptions(OperatingSystems, (loaded?.Platforms.Values ?? []).Select(profile => new ProfileOption(profile.Id, profile)
        {
            IsChecked = remembered ? Settings.CheckedOperatingSystems.Contains(profile.Id) : profile.Id == identity.Platform,
        }));
        AddOptions(Computers, (loaded?.Computers.Values ?? []).Select(profile => new ProfileOption(ComputerLabel(profile), profile)
        {
            IsChecked = remembered && Settings.CheckedComputers.Contains(profile.Id),
        }));
        HostName = HostNameFor(loaded, identity);
        LoadingStage = Strings.ReadingDevices;
    }

    /// <summary>The USB tree is read; the display count is read next, off the UI thread.</summary>
    public void ReadingDisplays() => LoadingStage = Strings.ReadingDisplays;

    /// <summary>What the window is reading while <see cref="IsLoading"/>.</summary>
    public string LoadingStage
    {
        get => loadingStage;
        private set => SetField(ref loadingStage, value);
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
    public string HostName
    {
        get;
        private set => SetField(ref field, value);
    }

    /// <summary>Platform profiles: generic limits, for computers without a profile of their own. Empty until the database is loaded.</summary>
    public ObservableCollection<ProfileOption> OperatingSystems { get; } = [];

    public ObservableCollection<ProfileOption> Computers { get; } = [];

    private void AddOptions(ObservableCollection<ProfileOption> list, IEnumerable<ProfileOption> options)
    {
        foreach (var option in options.OrderBy(option => option.Label, StringComparer.CurrentCultureIgnoreCase))
        {
            option.PropertyChanged += (_, _) =>
            {
                log.ProfileChecked(option.Label, option.IsChecked);
                KeepCheckedProfiles();
                Refresh();
            };
            list.Add(option);
        }
    }

    // Kept only while "remember" is on; see SettingsViewModel.CheckedProfiles.
    private void KeepCheckedProfiles() => Settings.CheckedProfiles(
        [.. OperatingSystems.Where(option => option.IsChecked).Select(option => option.Profile.Id)],
        [.. Computers.Where(option => option.IsChecked).Select(option => option.Profile.Id)]);

    private IReadOnlyList<ProfileOption> Checked => [.. OperatingSystems.Where(option => option.IsChecked), .. Computers.Where(option => option.IsChecked)];

    public bool NothingChecked => StatusRows.Count == 0;

    public string DetectedComputer { get; }

    /// <summary>A profile can only be exported when the computer reports manufacturer and model: its id comes from them.</summary>
    public bool CanExport => !IsLoading && !string.IsNullOrWhiteSpace(identity.Manufacturer) && !string.IsNullOrWhiteSpace(identity.Model);

    public string ExportFileName => ComputerProfileExport.FileName(identity);

    /// <summary>This computer as a computer profile YAML, to send to the developers.</summary>
    public string ExportProfile() =>
        ComputerProfileExport.ToYaml(identity, tree, readExternalDisplays(), AppVersion.Current, DateOnly.FromDateTime(DateTime.Now));

    /// <summary>The view that carries out the commands below; set by the main window.</summary>
    public IMainWindowActions? Window { get; set; }

    /// <summary>File > Export computer profile and the top bar button; enabled when <see cref="CanExport"/>.</summary>
    public RelayCommand ExportProfileCommand { get; }

    /// <summary>File > Export log: the same export as the log window's button.</summary>
    public RelayCommand ExportLogCommand { get; }

    public RelayCommand ExitCommand { get; }

    public RelayCommand AboutCommand { get; }

    /// <summary>File > Settings.</summary>
    public RelayCommand SettingsCommand { get; }

    /// <summary>The settings window's content; also read by the update check.</summary>
    public SettingsViewModel Settings { get; }

    public IReadOnlyList<string> DatabaseErrors { get; private set; } = [];

    /// <summary>The update notice (#123); null where the app does not check for updates.</summary>
    public UpdateViewModel? Update { get; init; }

    /// <summary>The log window's content.</summary>
    public LogViewModel Log { get; }

    public bool HasDatabaseErrors => DatabaseErrors.Count > 0;

    /// <summary>True until the first USB tree arrives; the window is shown before anything is read.</summary>
    public bool IsLoading
    {
        get => isLoading;
        set
        {
            if (SetField(ref isLoading, value))
            {
                OnPropertyChanged(nameof(CanExport)); // Not before the USB tree is known.
                ExportProfileCommand.Refresh();
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
            if (SetField(ref selectedNode, value) && value != null && Roots.Count > 0)
            {
                // Owner decision in #68: clicking a controller, or anything under it, selects that controller's tree.
                allControllers = value == Roots[0];
                selectedControllerId = Roots[0].Children.FirstOrDefault(controller => Contains(controller, value))?.Id;
                Refresh();
            }
        }
    }

    private static bool Contains(NodeViewModel item, NodeViewModel wanted) =>
        item == wanted || item.Children.Any(child => Contains(child, wanted));

    /// <summary>The controller whose tree the status field and findings cover; null when they cover all controllers.</summary>
    public string? SelectedControllerId => allControllers ? null : selectedControllerId;

    /// <summary>What the status field covers: the selected controller, or all controllers.</summary>
    public string StatusScope => string.Format(CultureInfo.CurrentCulture, Strings.StatusFor,
        tree.FirstOrDefault(controller => controller.Id == SelectedControllerId)?.Name ?? Strings.AllControllers);

    // A mark for an active cable or extender the operating system does not show, on the line from a port: the extra
    // hops count in every tier and hop check of everything behind the port. 0 removes the mark.
    private void Mark(string portId, int hops)
    {
        log.CableMark(UsbTree.Find(tree, portId)?.Children.FirstOrDefault()?.Name ?? portId, hops);
        if (hops == 0)
        {
            extraHops.Remove(portId);
        }
        else
        {
            extraHops[portId] = hops;
        }
        Refresh();
    }

    public string OverallText => string.Format(CultureInfo.CurrentCulture, Strings.Overall, StatusColors.Text(report.Overall));

    public string OverallColor => StatusColors.Hex(report.Overall);

    /// <summary>One row per checked profile: operating systems first, then computers.</summary>
    public ObservableCollection<StatusRow> StatusRows { get; } = [];

    /// <summary>Devices that are gone, then devices that disconnected and came back; each can be cleared.</summary>
    public ObservableCollection<DisconnectedItem> Disconnected { get; } = [];

    public ObservableCollection<FindingItem> Findings { get; } = [];

    public void Load(IReadOnlyList<UsbNode> detected) => Load(detected, readExternalDisplays());

    /// <param name="displays">The external display count, read off the UI thread; later refreshes read it again.</param>
    public void Load(IReadOnlyList<UsbNode> detected, int? displays)
    {
        IsLoading = false;
        log.TreeRead(detected);
        tree = detected;
        history.Record(tree);
        Refresh(displays);
    }

    public void Apply(IReadOnlyList<UsbChange> changes)
    {
        log.Changes(tree, changes);
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

    private void Refresh() => Refresh(readExternalDisplays());

    private void Refresh(int? displays)
    {
        var options = Checked;
        var profiles = options.Select(option => option.Profile).ToList();
        var reconnected = history.Reconnected(tree);
        var reconnectedIds = reconnected.Select(device => device.Node.Id).ToList();
        var missing = history.Missing(tree);
        var all = DiagnosticEvaluator.EvaluateAll(tree, profiles, missing, displays, reconnectedIds, extraHops);
        // The log gets every finding of every checked profile for all controllers, the unknown ones included.
        log.Findings(options.Count == 0
            ? all.Counts.Findings.Select(finding => ((string?)null, finding))
            : all.Profiles.SelectMany((result, i) => result.Report.Findings.Select(finding => ((string?)options[i].Label, finding))));
        // The tree needs only each line's worst status and the missing devices, which are the same for every profile.
        var lines = all.Counts with { Connections = all.Connections };

        // Owner decision in #68: the status and findings cover the selected controller's tree. At start, and when it is
        // gone, the controller with the most connected hubs and devices (built-in hubs not counted) is selected; on a
        // tie the first.
        if (!allControllers && tree.All(controller => controller.Id != selectedControllerId))
        {
            selectedControllerId = tree.MaxBy(controller => TopologyCounts.Depths([controller]).Count)?.Id;
        }
        var selected = tree.FirstOrDefault(controller => controller.Id == SelectedControllerId);
        report = selected == null
            ? all
            : DiagnosticEvaluator.EvaluateAll([selected], profiles, [.. missing.Where(device => device.AncestorIds.Contains(selected.Id))], displays,
                reconnectedIds, extraHops, tree);
        OnPropertyChanged(nameof(SelectedControllerId));
        OnPropertyChanged(nameof(StatusScope));

        if (Roots.Count == 0)
        {
            Roots.Add(NodeViewModel.Host(HostName, tree, lines, extraHops, Mark));
        }
        else
        {
            Roots[0].UpdateHost(HostName, tree, lines, extraHops);
        }
        // Owner decision in #100: when the controllers first arrive, only the selected one, the one with most devices, is
        // open. After that the user's choices are kept.
        if (!controllersShown && tree.Count > 0)
        {
            controllersShown = true;
            foreach (var controller in Roots[0].Children)
            {
                controller.IsExpanded = controller.Id == selectedControllerId;
            }
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
        foreach (var device in all.Counts.Missing)
        {
            Disconnected.Add(DisconnectedItem(Strings.DisconnectedFrom, device));
        }
        foreach (var device in reconnected)
        {
            Disconnected.Add(DisconnectedItem(Strings.ReconnectedOn, device));
        }
        Findings.Clear();
        if (options.Count == 0)
        {
            foreach (var finding in report.Counts.Findings.Where(finding => finding.IsReal))
            {
                Findings.Add(new FindingItem(FindingHeader(finding), finding.Brief));
            }
            return;
        }
        // Owner decision in #68 (rule in #72): only the strictest checked profile's findings are listed, named after it
        // when several are checked. Detected facts (errors, disconnections) are listed once, unnamed, whichever profile reports them.
        // Owner decision in #85: only real findings are listed; unknowns and database facts are in the log only.
        var strictest = StrictestProfile.Of(profiles);
        var findings = report.Profiles[strictest].Report.Findings
            .Concat(report.Profiles.SelectMany(result => result.Report.Findings).Where(finding => finding.Kind == FactKind.DetectedFact))
            .Where(finding => finding.IsReal)
            .Distinct();
        foreach (var finding in findings)
        {
            Findings.Add(new FindingItem(options.Count == 1 || finding.Kind == FactKind.DetectedFact
                ? FindingHeader(finding)
                : string.Format(CultureInfo.CurrentCulture, Strings.FindingHeaderForProfile, options[strictest].Label, KindText(finding.Kind), finding.SubjectName),
                finding.Brief));
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
            StateText(displays.State));

    private static string LimitText(string label, LimitCheck limit) =>
        string.Format(CultureInfo.CurrentCulture, Strings.LimitLine, label, limit.Actual,
            limit.Limit?.ToString(CultureInfo.CurrentCulture) ?? Strings.UnknownValue,
            StateText(limit.State));

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
