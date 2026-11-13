using System.Collections.ObjectModel;
using System.Globalization;
using UsbHelper.App.Licensing;
using UsbHelper.App.Report;
using UsbHelper.App.Resources;
using UsbHelper.Core.Database;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;
using UsbHelper.Core.Export;
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
/// <see cref="Load(IReadOnlyList{UsbNode}, IReadOnlyList{DisplayMonitor})"/> once each, in that order, then
/// <see cref="Apply"/> for every batch of USB changes and <see cref="DisplaysChanged"/> for every display change, on the
/// UI thread.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly SessionHistory history = new();
    private readonly EventLog log;
    private readonly Func<IReadOnlyList<DisplayMonitor>?> readMonitors;
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
    // Owner decision in #208: until the user selects an item or opens or closes a controller, the selection and the open
    // controller follow what is plugged in. Refresh follows again.
    private bool following = true;
    // Set while the app itself opens and closes controllers, so that is not taken for the user's choice.
    private bool laying;
    // Connected hubs and devices per controller id at the last refresh, to see which controller grew.
    private Dictionary<string, int> controllerSizes = [];
    // The controller items whose open or closed state is watched for the user's choice.
    private readonly HashSet<NodeViewModel> watched = [];
    // The last refresh's monitors and its result for all controllers, for the setup export.
    private IReadOnlyList<DisplayMonitor>? monitors;
    private SystemsReport lastReport = DiagnosticEvaluator.EvaluateAll([], [], []);

    /// <summary>A model whose database is already loaded (or failed to load): it waits for the USB tree.</summary>
    public MainViewModel(
        HardwareDatabase? database, IReadOnlyList<DatabaseError> databaseErrors, ComputerIdentity identity,
        Func<IReadOnlyList<DisplayMonitor>?>? readMonitors = null, EventLog? log = null, SettingsViewModel? settings = null,
        CodeEntitlements? entitlements = null)
        : this(identity, readMonitors, log, settings, entitlements)
    {
        LoadDatabase(database, databaseErrors);
    }

    /// <summary>A model for the window before anything is read: it shows "Loading database..." first.</summary>
    /// <param name="readMonitors">Reads the active monitors, or null when unknown. Called on every refresh.</param>
    /// <param name="log">The session's event log; a new one when null.</param>
    /// <param name="settings">The settings (#126); the defaults, kept in memory only, when null.</param>
    /// <param name="entitlements">The unlock code (#234); from this build's keys and <paramref name="settings"/> when null.</param>
    public MainViewModel(
        ComputerIdentity identity, Func<IReadOnlyList<DisplayMonitor>?>? readMonitors = null, EventLog? log = null, SettingsViewModel? settings = null,
        CodeEntitlements? entitlements = null)
    {
        Settings = settings ?? new SettingsViewModel();
        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.RememberCheckedProfiles))
            {
                KeepCheckedProfiles(); // Turned on: keep what is checked now.
            }
            if (e.PropertyName is nameof(SettingsViewModel.Branding) or nameof(SettingsViewModel.CompanyName) or nameof(SettingsViewModel.LogoPreview))
            {
                OnPropertyChanged(nameof(ShowsBranding));
                OnPropertyChanged(nameof(BrandingName));
                OnPropertyChanged(nameof(BrandingLogo)); // Also when a new logo replaced the old one under the same path.
            }
        };
        this.log = log ?? new EventLog();
        entitlements ??= new CodeEntitlements(LicenceKeys.All, Settings, this.log);
        Entitlements = entitlements;
        Settings.Gate(entitlements);
        Unlock = new UnlockViewModel(entitlements);
        Log = new LogViewModel(this.log);
        this.readMonitors = readMonitors ?? (() => null);
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
        CopyTreeCommand = new RelayCommand(() => Window?.CopyText(TreeText.Render(Roots[0])), () => Roots.Count > 0);
        ExportSetupCommand = new RelayCommand(() => Window?.ExportSetup(), () => Roots.Count > 0);
        RefreshCommand = new RelayCommand(() => Window?.ConfirmRefresh(), () => !IsLoading);
    }

    /// <summary>File > Refresh and the toolbar's Refresh button (#206): the window asks what to clear.</summary>
    public RelayCommand RefreshCommand { get; }

    /// <summary>
    /// Raised by <see cref="RequestRefresh"/>: whoever reads the USB tree reads it again and calls
    /// <see cref="Load(IReadOnlyList{UsbNode}, IReadOnlyList{DisplayMonitor})"/>.
    /// </summary>
    public event Action? RefreshRequested;

    /// <summary>
    /// Reads the USB tree and the displays again (#206). With <paramref name="clearLog"/> it is a fresh start: the log is
    /// emptied and the disconnected and reconnected devices are forgotten. Cable marks and checked profiles stay.
    /// Does nothing when nobody reads the tree (an operating system without detection), or while a read is running, so
    /// two reads never overlap.
    /// </summary>
    public void RequestRefresh(bool clearLog)
    {
        if (RefreshRequested is null || IsLoading)
        {
            return;
        }
        if (clearLog)
        {
            log.Clear();
            history.Reset();
            log.Add("Refresh: log and disconnected devices cleared");
        }
        else
        {
            log.Add("Refresh");
        }
        // #208: the fresh read picks the busiest controller again, as at start, and follows from there.
        following = true;
        allControllers = false;
        selectedControllerId = null;
        controllerSizes = [];
        SelectedNode = null;
        LoadingStage = Strings.ReadingDevices;
        IsLoading = true;
        RefreshRequested();
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
        ComputerProfileExport.ToYaml(identity, tree, DisplayMonitor.ExternalCount(readMonitors()), AppVersion.Current, DateOnly.FromDateTime(DateTime.Now));

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

    /// <summary>Edit > Copy tree as text (#134): the same text the log gets.</summary>
    public RelayCommand CopyTreeCommand { get; }

    /// <summary>File > Export full setup (#135).</summary>
    public RelayCommand ExportSetupCommand { get; }

    /// <summary>The room or system name the user typed (#186), for the setup export's file name. Not saved between runs.</summary>
    public string RoomName
    {
        get => roomName;
        set => SetField(ref roomName, value ?? "");
    }

    private string roomName = "";

    /// <summary>
    /// <c>usb-helper_&lt;room or system name&gt;_YYYY-MM-DD_HH-mm-ss.zip</c>, or without the name when none is entered (#186);
    /// the save dialog lets the user change it. Characters a file name cannot hold become <c>-</c>.
    /// </summary>
    public string SetupFileName(DateTime time)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['\\', '/', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var room = new string(RoomName.Select(c => invalid.Contains(c) || char.IsControl(c) ? '-' : c).ToArray()).Trim();
        var stamp = time.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
        return room.Length == 0 ? $"usb-helper_{stamp}.zip" : $"usb-helper_{room}_{stamp}.zip";
    }

    /// <summary>
    /// Writes the full setup export (#135) to <paramref name="output"/>: setup.json, the tree as text, the window's
    /// <paramref name="screenshot"/> (PNG) and the log.
    /// </summary>
    public void ExportSetup(Stream output, byte[] screenshot, DateOnly date, string? appVersion = null)
    {
        SetupZip.Write(output, Setup(date, appVersion ?? AppVersion.Current).ToJson(), TreeText.Render(Roots[0]), screenshot, log.Export());
        log.Add("Setup exported");
    }

    // Everything detected and found, for all controllers: each checked profile's result with every finding, the unknown
    // ones included.
    private SetupFile Setup(DateOnly date, string appVersion)
    {
        var (containerFree, displays) = SetupFile.AnonymizeContainers(tree, monitors);
        var (controllers, marks) = SetupFile.Anonymize(containerFree, extraHops);
        var options = Checked;
        IReadOnlyList<SetupResult> results = options.Count == 0
            ? [new SetupResult(null, "No profile checked", lastReport.Overall, Exported(lastReport.Counts.Findings))]
            : [.. options.Select((option, i) => new SetupResult(option.Profile.Id, option.Label, lastReport.Profiles[i].Report.Overall,
                Exported(lastReport.Profiles[i].Report.Findings)))];
        // No computer name: it is often an asset or service tag, a serial number. Manufacturer and model find the profile.
        return new SetupFile(appVersion, date, identity with { MachineName = null }, [.. options.Select(option => option.Profile.Id)], controllers, marks, displays, results);
    }

    private static List<SetupFinding> Exported(IEnumerable<Finding> findings) =>
        [.. findings.Select(finding => new SetupFinding(finding.Kind, finding.SubjectName, finding.Message))];

    /// <summary>
    /// The PDF sign-off report's content (#231): the same as the setup export, for all controllers, with every finding of
    /// every checked profile. A detected fact is listed once, without a profile.
    /// </summary>
    public ReportModel Report(DateTime created, string? companyName, string? logoPath, string appVersion)
    {
        var setup = Setup(DateOnly.FromDateTime(created), appVersion);
        var several = setup.Results.Count > 1;
        var findings = setup.Results
            .SelectMany(result => result.Findings.Select(finding => new ReportFinding(finding.Kind,
                several && finding.Kind != FactKind.DetectedFact ? result.Label : null, finding.Subject, finding.Message)))
            .Distinct()
            .ToList();
        var counts = lastReport.Counts;
        var perBus = TopologyCounts.PerBus(tree, extraHops);
        var atLeast = TopologyCounts.Unreadable(tree).Any() || (perBus.AnyBusKnown && perBus.BusUnknown);
        IReadOnlyList<DiagnosticReport> reports = lastReport.Profiles.Count == 0 ? [counts] : [.. lastReport.Profiles.Select(result => result.Report)];
        var profiles = reports.Select((result, i) => setup.Results[i].Profile == null
            ? new ReportProfile(Strings.ReportNoProfile, result.Overall, [])
            : new ReportProfile(setup.Results[i].Label, result.Overall, ReportChecks(result))).ToList();
        var placement = MonitorPlacement.Place(tree, monitors ?? []);
        var items = Flat(Roots[0]).ToDictionary(item => item.Id, item => item.Text);
        var displays = monitors?.Select(monitor => new ReportDisplay(monitor.Name ?? Strings.MonitorUnnamed, NodeViewModel.OutputText(monitor.Output),
            placement.Linked.FirstOrDefault(link => link.Value.Contains(monitor)).Key is { } node ? items.GetValueOrDefault(node) : null)).ToList();
        return new ReportModel(companyName, logoPath, string.IsNullOrWhiteSpace(RoomName) ? null : RoomName.Trim(), appVersion, created,
            setup.Computer, lastReport.Overall,
            new ReportCounts(counts.Hubs.Actual, perBus.AnyBusKnown ? perBus.Usb2.Hubs : null, perBus.AnyBusKnown ? perBus.Usb3.Hubs : null,
                counts.Tiers.Actual, counts.Hops.Actual, atLeast, counts.Displays.ExternalDisplays),
            profiles, displays, ReportRow.Of(Roots[0]), findings);
    }

    /// <summary>
    /// A profile's checks for the report, as the app judges them. The recommended depth only when it is known and the
    /// maximum is kept, the same as the tree's lines; disconnected devices only when there are.
    /// </summary>
    public static List<ReportCheck> ReportChecks(DiagnosticReport result)
    {
        static ConnectionStatus Status(LimitState state, ConnectionStatus exceeded = ConnectionStatus.Purple) => state switch
        {
            LimitState.Within => ConnectionStatus.Green,
            LimitState.Exceeded => exceeded,
            _ => ConnectionStatus.Grey,
        };
        var checks = new List<ReportCheck>
        {
            new(Strings.LimitHubs, result.Hubs.Limit, result.Hubs.Actual, Status(result.Hubs.State)),
            new(Strings.LimitTiers, result.Tiers.Limit, result.Tiers.Actual, Status(result.Tiers.State)),
            new(Strings.LimitHops, result.Hops.Limit, result.Hops.Actual, Status(result.Hops.State)),
            new(Strings.LimitDisplays, result.Displays.MaxExternal, result.Displays.ExternalDisplays, Status(result.Displays.State)),
        };
        if (result.RecommendedHops is { Limit: not null } recommended && result.Hops.State != LimitState.Exceeded)
        {
            checks.Add(new(Strings.LimitRecommendedHops, recommended.Limit, recommended.Actual, Status(recommended.State, ConnectionStatus.Orange)));
        }
        if (result.Missing.Count > 0)
        {
            checks.Add(new(Strings.LimitDisconnected, null, result.Missing.Count, ConnectionStatus.Red));
        }
        return checks;
    }

    private static IEnumerable<NodeViewModel> Flat(NodeViewModel item) => item.Children.SelectMany(Flat).Prepend(item);

    /// <summary>The settings window's content; also read by the update check.</summary>
    public SettingsViewModel Settings { get; }

    /// <summary>What is unlocked (#234). Every feature check goes through this.</summary>
    public IEntitlements Entitlements { get; }

    /// <summary>True when the top bar shows the company logo or name (#229): unlocked, and at least one is set.</summary>
    public bool ShowsBranding => Settings.Branding != null;

    /// <summary>The company name in the top bar, only when there is no logo: never both. Null when locked or none.</summary>
    public string? BrandingName => Settings.Branding is { LogoOriginal: null } branding ? branding.CompanyName : null;

    /// <summary>The logo 64 pixels high for the top bar (32 high at 200% scale), or null when locked or there is no logo.</summary>
    public string? BrandingLogo => Settings.Branding?.LogoPngs.GetValueOrDefault(64);

    /// <summary>The settings window's Support the project section.</summary>
    public UnlockViewModel Unlock { get; }

    public IReadOnlyList<string> DatabaseErrors { get; private set; } = [];

    /// <summary>The update popup's model (#123, #131); null where the app does not check for updates.</summary>
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
                RefreshCommand.Refresh();
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
            // The Displays group and its monitors belong to no controller (#133): clicking them keeps the scope. A monitor
            // shown under a USB node (#143) is part of that controller's tree, like anything else under it.
            if (!SetField(ref selectedNode, value) || value == null)
            {
                return;
            }
            following = false; // #208: selecting anything, a monitor too, stops following.
            if (Roots.Count > 0 && !IsDisplay(value))
            {
                // Owner decision in #68: clicking a controller, or anything under it, selects that controller's tree.
                allControllers = value == Roots[0];
                selectedControllerId = Roots[0].Children.FirstOrDefault(controller => Contains(controller, value))?.Id;
                Refresh();
            }
        }
    }

    private bool IsDisplay(NodeViewModel item) =>
        Roots[0].Children.FirstOrDefault(child => child.Id == NodeViewModel.DisplaysId) is { } displays && Contains(displays, item);

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

    public void Load(IReadOnlyList<UsbNode> detected) => Load(detected, readMonitors());

    /// <param name="monitors">The active monitors, read off the UI thread; later refreshes read them again.</param>
    public void Load(IReadOnlyList<UsbNode> detected, IReadOnlyList<DisplayMonitor>? monitors)
    {
        IsLoading = false;
        log.TreeRead(detected);
        tree = detected;
        history.Record(tree);
        Refresh(monitors);
        LogTree();
        CopyTreeCommand.Refresh();
        ExportSetupCommand.Refresh();
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
        LogTree();
    }

    /// <summary>A monitor was connected or removed (#133): the Displays group, the display count and the log follow.</summary>
    public void DisplaysChanged()
    {
        if (!IsLoading && Roots.Count > 0)
        {
            var before = TreeText.Render(Roots[0]);
            Refresh();
            // Windows also reports changes nobody sees, such as a moved taskbar: log the tree only when this change shows.
            if (TreeText.Render(Roots[0]) != before)
            {
                LogTree();
            }
        }
    }

    // The tree as it looks now, after the first read and every change (#134).
    private void LogTree() => log.Add("Tree:" + Environment.NewLine + TreeText.Render(Roots[0]));

    private void Refresh() => Refresh(readMonitors());

    private void Refresh(IReadOnlyList<DisplayMonitor>? monitors)
    {
        this.monitors = monitors;
        log.Displays(monitors);
        var displays = DisplayMonitor.ExternalCount(monitors);
        var options = Checked;
        var profiles = options.Select(option => option.Profile).ToList();
        var reconnected = history.Reconnected(tree);
        var reconnectedIds = reconnected.Select(device => device.Node.Id).ToList();
        var missing = history.Missing(tree);
        var all = DiagnosticEvaluator.EvaluateAll(tree, profiles, missing, displays, reconnectedIds, extraHops, monitors: monitors);
        lastReport = all;
        // The log gets every finding of every checked profile for all controllers, the unknown ones included.
        log.Findings(options.Count == 0
            ? all.Counts.Findings.Select(finding => ((string?)null, finding))
            : all.Profiles.SelectMany((result, i) => result.Report.Findings.Select(finding => ((string?)options[i].Label, finding))));
        // The tree needs only each line's worst status and the missing devices, which are the same for every profile.
        var lines = all.Counts with { Connections = all.Connections };

        // Owner decision in #68: the status and findings cover the selected controller's tree. At start, and when it is
        // gone, the controller with the most connected hubs and devices (built-in hubs not counted) is selected; on a
        // tie the first.
        // Owner decision in #208: while following, a controller that grew and now has strictly more connected hubs and
        // devices than the selected one is selected; several: the biggest, on a tie the first. The selected controller
        // stays when it shrinks, so a brief drop does not jump away and its disconnected devices stay in view.
        // #212: for following, a controller's size counts its disconnected devices too, so a dropped dock keeps its place
        // against a small device plugged in elsewhere. The busiest pick above (#68) counts connected ones only.
        var connected = tree.ToDictionary(controller => controller.Id, controller => TopologyCounts.Depths([controller]).Count);
        var sizes = tree.ToDictionary(controller => controller.Id,
            controller => connected[controller.Id] + missing.Count(device => device.AncestorIds.Contains(controller.Id)));
        if (!allControllers && tree.All(controller => controller.Id != selectedControllerId))
        {
            selectedControllerId = tree.MaxBy(controller => connected[controller.Id])?.Id;
        }
        else if (following && selectedControllerId != null)
        {
            var current = sizes[selectedControllerId];
            selectedControllerId = tree
                .Where(controller => sizes[controller.Id] > controllerSizes.GetValueOrDefault(controller.Id) && sizes[controller.Id] > current)
                .MaxBy(controller => sizes[controller.Id])?.Id ?? selectedControllerId;
        }
        controllerSizes = sizes;
        var selected = tree.FirstOrDefault(controller => controller.Id == SelectedControllerId);
        report = selected == null
            ? all
            : DiagnosticEvaluator.EvaluateAll([selected], profiles, [.. missing.Where(device => device.AncestorIds.Contains(selected.Id))], displays,
                reconnectedIds, extraHops, tree, monitors);
        OnPropertyChanged(nameof(SelectedControllerId));
        OnPropertyChanged(nameof(StatusScope));

        var placement = MonitorPlacement.Place(tree, monitors ?? []);
        log.DisplayLinks(placement, tree);
        if (Roots.Count == 0)
        {
            Roots.Add(NodeViewModel.Host(HostName, tree, lines, extraHops, Mark, placement));
        }
        else
        {
            Roots[0].UpdateHost(HostName, tree, lines, extraHops, placement);
        }
        // Owner decisions in #142 and #208: while following, only the selected controller is open, and only when it has
        // devices, connected or disconnected. Once the user opens or closes a controller, their choices are kept.
        if (following)
        {
            var open = selected != null && sizes[selected.Id] > 0 ? selected.Id : null;
            laying = true;
            foreach (var controller in Roots[0].Children.Where(item => item.CanExpand))
            {
                controller.IsExpanded = controller.Id == open;
            }
            laying = false;
        }
        foreach (var controller in Roots[0].Children.Where(item => item.Id != NodeViewModel.DisplaysId && watched.Add(item)))
        {
            controller.PropertyChanged += (sender, e) =>
            {
                if (!laying && e.PropertyName == nameof(NodeViewModel.IsExpanded) && sender is NodeViewModel { CanExpand: true })
                {
                    following = false;
                }
            };
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
