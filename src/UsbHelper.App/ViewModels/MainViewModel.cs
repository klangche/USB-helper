using System.Collections.ObjectModel;
using System.Globalization;
using UsbHelper.App.Resources;
using UsbHelper.Core.Database;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;

namespace UsbHelper.App.ViewModels;

/// <param name="Profile">A computer or platform profile, or null for "no profile".</param>
public sealed record ProfileOption(string Label, ResolvedProfile? Profile);

/// <param name="Header">Kind (detected, database, risk, unknown) and subject.</param>
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
    private IReadOnlyList<UsbNode> tree = [];
    private ProfileOption selectedProfile;
    private NodeViewModel? selectedNode;
    private string? notice;
    private DiagnosticReport report;

    /// <param name="readExternalDisplays">Reads the active external display count, or null when unknown. Called on every refresh.</param>
    public MainViewModel(
        HardwareDatabase? database, IReadOnlyList<DatabaseError> databaseErrors, ComputerIdentity identity, Func<int?>? readExternalDisplays = null)
    {
        this.readExternalDisplays = readExternalDisplays ?? (() => null);
        DatabaseErrors = databaseErrors.Select(error => error.ToString()).ToList();
        var none = new ProfileOption(Strings.NoProfile, null);
        Profiles =
        [
            none,
            .. (database?.Computers.Values ?? [])
                .Select(profile => new ProfileOption($"{profile.Find("manufacturer")?.Scalar} {profile.Find("model")?.Scalar} ({profile.Id})", profile))
                .OrderBy(option => option.Label, StringComparer.CurrentCultureIgnoreCase),
            // Generic platform limits, for computers without a profile of their own.
            .. (database?.Platforms.Values ?? [])
                .Select(profile => new ProfileOption(string.Format(CultureInfo.CurrentCulture, Strings.PlatformProfile, profile.Id), profile))
                .OrderBy(option => option.Label, StringComparer.CurrentCultureIgnoreCase),
        ];
        var match = database == null ? null : ProfileMatcher.Match(database, identity);
        selectedProfile = Profiles.FirstOrDefault(option => option.Profile != null && option.Profile == match) ?? none;
        DetectedComputer = string.Format(CultureInfo.CurrentCulture, Strings.DetectedComputer,
            identity.Manufacturer ?? Strings.UnknownValue, identity.Model ?? Strings.UnknownValue);
        report = DiagnosticEvaluator.Evaluate(tree, selectedProfile.Profile, []);
    }

    public IReadOnlyList<ProfileOption> Profiles { get; }

    /// <summary>Picked automatically only on an exact manufacturer and model match; otherwise the user picks.</summary>
    public ProfileOption SelectedProfile
    {
        get => selectedProfile;
        set
        {
            if (SetField(ref selectedProfile, value))
            {
                Refresh();
            }
        }
    }

    public string DetectedComputer { get; }

    public IReadOnlyList<string> DatabaseErrors { get; }

    public bool HasDatabaseErrors => DatabaseErrors.Count > 0;

    /// <summary>A message shown instead of the tree, for example when detection is not available.</summary>
    public string? Notice
    {
        get => notice;
        set => SetField(ref notice, value);
    }

    public ObservableCollection<NodeViewModel> Roots { get; } = [];

    public NodeViewModel? SelectedNode
    {
        get => selectedNode;
        set => SetField(ref selectedNode, value);
    }

    public string OverallText => string.Format(CultureInfo.CurrentCulture, Strings.Overall, StatusColors.Text(report.Overall));

    public string OverallColor => StatusColors.Hex(report.Overall);

    public string HubsText => LimitText(Strings.LimitHubs, report.Hubs);

    public string TiersText => LimitText(Strings.LimitTiers, report.Tiers);

    public string HopsText => LimitText(Strings.LimitHops, report.Hops);

    public string DisplaysText =>
        string.Format(CultureInfo.CurrentCulture, Strings.LimitLine, Strings.LimitDisplays,
            report.Displays.ExternalDisplays?.ToString(CultureInfo.CurrentCulture) ?? Strings.UnknownValue,
            report.Displays.MaxExternal?.ToString(CultureInfo.CurrentCulture) ?? Strings.UnknownValue,
            StateText(report.Displays.State),
            report.Displays.Verified ? Strings.Verified : Strings.Unverified);

    public ObservableCollection<DisconnectedItem> Disconnected { get; } = [];

    public ObservableCollection<FindingItem> Findings { get; } = [];

    public void Load(IReadOnlyList<UsbNode> detected)
    {
        tree = detected;
        history.Record(tree);
        Refresh();
    }

    public void Apply(IReadOnlyList<UsbChange> changes)
    {
        tree = UsbTree.Apply(tree, changes);
        history.Record(tree);
        Refresh();
    }

    private void Refresh()
    {
        report = DiagnosticEvaluator.Evaluate(tree, SelectedProfile.Profile, history.Missing(tree), readExternalDisplays());

        for (var i = 0; i < tree.Count; i++)
        {
            var existing = Roots.FirstOrDefault(item => item.Id == tree[i].Id);
            if (existing == null)
            {
                Roots.Insert(i, NodeViewModel.Create(tree[i], report));
                continue;
            }
            if (Roots.IndexOf(existing) != i)
            {
                Roots.Move(Roots.IndexOf(existing), i);
            }
            existing.Update(tree[i], report);
        }
        while (Roots.Count > tree.Count)
        {
            Roots.RemoveAt(Roots.Count - 1);
        }

        OnPropertyChanged(nameof(OverallText));
        OnPropertyChanged(nameof(OverallColor));
        OnPropertyChanged(nameof(HubsText));
        OnPropertyChanged(nameof(TiersText));
        OnPropertyChanged(nameof(HopsText));
        OnPropertyChanged(nameof(DisplaysText));

        Disconnected.Clear();
        foreach (var missing in report.Missing)
        {
            var id = missing.Node.Id;
            Disconnected.Add(new DisconnectedItem(
                string.Format(CultureInfo.CurrentCulture, Strings.DisconnectedFrom, missing.Node.Name, missing.PortName),
                new RelayCommand(() =>
                {
                    history.Clear(id);
                    Refresh();
                })));
        }
        Findings.Clear();
        foreach (var finding in report.Findings)
        {
            Findings.Add(new FindingItem(
                string.Format(CultureInfo.CurrentCulture, Strings.FindingHeader, KindText(finding.Kind), finding.SubjectName),
                finding.Message));
        }
    }

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
