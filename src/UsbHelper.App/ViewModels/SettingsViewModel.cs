using UsbHelper.App.Settings;
using UsbHelper.Core.Diagnostics;

namespace UsbHelper.App.ViewModels;

/// <summary>
/// The settings (#126), shown in the settings window and read by the update check and the profile lists. Every change
/// is written to settings.json at once; a failed write is logged and the setting still applies until the app closes.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly SettingsFile? file;
    private readonly EventLog? log;
    private AppSettings current;

    /// <summary>Default settings kept in memory only, for tests and for a model without a file.</summary>
    public SettingsViewModel()
        : this(null, null, new AppSettings())
    {
    }

    private SettingsViewModel(SettingsFile? file, EventLog? log, AppSettings current)
    {
        this.file = file;
        this.log = log;
        this.current = current;
    }

    /// <summary>The settings in <paramref name="file"/>, or the defaults; see <see cref="SettingsFile.Load"/>.</summary>
    public static SettingsViewModel Load(SettingsFile file, EventLog log) => new(file, log, file.Load(log));

    /// <summary>Where the settings are saved, shown in the settings window; null when they are kept in memory only.</summary>
    public string? FilePath => file?.Path;

    public bool CheckForUpdatesAtStart
    {
        get => current.CheckForUpdatesAtStart;
        set => Change(current with { CheckForUpdatesAtStart = value });
    }

    public bool OfferBetas
    {
        get => current.OfferBetas;
        set => Change(current with { OfferBetas = value });
    }

    /// <summary>Turning it off forgets the kept profiles; the main window keeps the current ones when it is turned on.</summary>
    public bool RememberCheckedProfiles
    {
        get => current.RememberCheckedProfiles;
        set => Change(value ? current with { RememberCheckedProfiles = true }
            : current with { RememberCheckedProfiles = false, CheckedOperatingSystems = [], CheckedComputers = [] });
    }

    public IReadOnlyList<string> CheckedOperatingSystems => current.CheckedOperatingSystems;

    public IReadOnlyList<string> CheckedComputers => current.CheckedComputers;

    /// <summary>The profiles checked now, by id; kept only while <see cref="RememberCheckedProfiles"/> is on.</summary>
    public void CheckedProfiles(IReadOnlyList<string> operatingSystems, IReadOnlyList<string> computers)
    {
        if (current.RememberCheckedProfiles)
        {
            Change(current with { CheckedOperatingSystems = operatingSystems, CheckedComputers = computers });
        }
    }

    private void Change(AppSettings changed)
    {
        if (Same(changed, current))
        {
            return;
        }
        var before = current;
        current = changed;
        try
        {
            file?.Save(current);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log?.Add($"Settings could not be saved to {file!.Path}: {e.Message}");
        }
        if (before.CheckForUpdatesAtStart != current.CheckForUpdatesAtStart)
        {
            OnPropertyChanged(nameof(CheckForUpdatesAtStart));
        }
        if (before.OfferBetas != current.OfferBetas)
        {
            OnPropertyChanged(nameof(OfferBetas));
        }
        if (before.RememberCheckedProfiles != current.RememberCheckedProfiles)
        {
            OnPropertyChanged(nameof(RememberCheckedProfiles));
        }
    }

    // The lists are compared by content, so an unchanged choice writes nothing.
    private static bool Same(AppSettings a, AppSettings b) =>
        a.CheckForUpdatesAtStart == b.CheckForUpdatesAtStart && a.OfferBetas == b.OfferBetas
        && a.RememberCheckedProfiles == b.RememberCheckedProfiles
        && a.CheckedOperatingSystems.SequenceEqual(b.CheckedOperatingSystems) && a.CheckedComputers.SequenceEqual(b.CheckedComputers);
}
