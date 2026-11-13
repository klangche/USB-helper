using System.Globalization;
using UsbHelper.App.Resources;
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
    private readonly CompanyLogo? logo;
    private readonly Func<bool> brandingUnlocked;
    private AppSettings current;
    private string? logoMessage;

    /// <summary>Default settings kept in memory only, for tests and for a model without a file.</summary>
    public SettingsViewModel()
        : this(null, null, new AppSettings(), null, () => true)
    {
    }

    private SettingsViewModel(SettingsFile? file, EventLog? log, AppSettings current, CompanyLogo? logo, Func<bool> brandingUnlocked)
    {
        this.file = file;
        this.log = log;
        this.current = current;
        this.logo = logo;
        this.brandingUnlocked = brandingUnlocked;
    }

    /// <summary>
    /// The settings in <paramref name="file"/>, or the defaults; see <see cref="SettingsFile.Load"/>. The company logo is
    /// kept in the branding folder next to the file. <paramref name="brandingUnlocked"/> gates <see cref="Branding"/>;
    /// without it branding is unlocked.
    /// </summary>
    public static SettingsViewModel Load(SettingsFile file, EventLog log, Func<bool>? brandingUnlocked = null) =>
        new(file, log, file.Load(log), new CompanyLogo(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(file.Path)!, "branding")),
            brandingUnlocked ?? (() => true));

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

    /// <summary>Trimmed when it is set; empty means none.</summary>
    public string CompanyName
    {
        get => current.CompanyName;
        set => Change(current with { CompanyName = value.Trim() });
    }

    /// <summary>The company name and logo for reports, or null when branding is locked or empty; see <see cref="Settings.Branding"/>.</summary>
    public Branding? Branding => Settings.Branding.Read(current.CompanyName, logo, brandingUnlocked());

    /// <summary>The logo 128 pixels high for the settings window, or null when there is no logo.</summary>
    public string? LogoPreview => logo?.Original == null ? null : logo.Png(128);

    /// <summary>Why the last chosen image was not used, or null.</summary>
    public string? LogoMessage
    {
        get => logoMessage;
        private set => SetField(ref logoMessage, value);
    }

    /// <summary>Makes <paramref name="path"/> the logo; a refused or failed import keeps the old one and says why.</summary>
    public void ImportLogo(string path) => ChangeLogo(() => logo?.Import(path));

    public void RemoveLogo() => ChangeLogo(() => logo?.Remove());

    private void ChangeLogo(Action change)
    {
        try
        {
            change();
            LogoMessage = null;
        }
        catch (InvalidDataException e)
        {
            LogoMessage = e.Message;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log?.Add($"Company logo in {logo!.Folder} could not be changed: {e.Message}");
            LogoMessage = string.Format(CultureInfo.CurrentCulture, Strings.LogoNotSaved, e.Message);
        }
        OnPropertyChanged(nameof(LogoPreview));
    }

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
        if (before.CompanyName != current.CompanyName)
        {
            OnPropertyChanged(nameof(CompanyName));
        }
    }

    // The lists are compared by content, so an unchanged choice writes nothing.
    private static bool Same(AppSettings a, AppSettings b) =>
        a.CheckForUpdatesAtStart == b.CheckForUpdatesAtStart && a.OfferBetas == b.OfferBetas
        && a.RememberCheckedProfiles == b.RememberCheckedProfiles && a.CompanyName == b.CompanyName
        && a.CheckedOperatingSystems.SequenceEqual(b.CheckedOperatingSystems) && a.CheckedComputers.SequenceEqual(b.CheckedComputers);
}
