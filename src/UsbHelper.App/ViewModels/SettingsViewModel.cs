using System.Globalization;
using UsbHelper.App.Licensing;
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
    private IEntitlements? entitlements;
    private AppSettings current;
    private string? logoMessage;

    /// <summary>Default settings kept in memory only, for tests and for a model without a file.</summary>
    public SettingsViewModel()
        : this(null, null, new AppSettings(), null)
    {
    }

    private SettingsViewModel(SettingsFile? file, EventLog? log, AppSettings current, CompanyLogo? logo)
    {
        this.file = file;
        this.log = log;
        this.current = current;
        this.logo = logo;
    }

    /// <summary>
    /// The settings in <paramref name="file"/>, or the defaults; see <see cref="SettingsFile.Load"/>. The company logo is
    /// kept in the branding folder next to the file; a logo from before the crop tool is upgraded here (#241).
    /// </summary>
    public static SettingsViewModel Load(SettingsFile file, EventLog log)
    {
        var logo = new CompanyLogo(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(file.Path)!, "branding"));
        logo.Upgrade();
        return new(file, log, file.Load(log), logo);
    }

    /// <summary>
    /// Unlocks branding and beta updates (#246) while <paramref name="unlock"/> has <see cref="Feature.Pro"/>, and follows
    /// its changes. Called once by <see cref="MainViewModel"/>: the entitlements read the unlock code from these settings,
    /// so they cannot be given to <see cref="Load"/>. Until then both are locked.
    /// </summary>
    public void Gate(IEntitlements unlock)
    {
        entitlements = unlock;
        unlock.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(BrandingUnlocked));
            OnPropertyChanged(nameof(Branding));
            OnPropertyChanged(nameof(BetasUnlocked));
        };
    }

    /// <summary>
    /// False until an unlock code gives <see cref="Feature.Pro"/>. While locked the name and logo cannot be changed, but
    /// the stored ones are kept and come back on unlock.
    /// </summary>
    public bool BrandingUnlocked => entitlements?.IsUnlocked(Feature.Pro) == true;

    /// <summary>Where the settings are saved, shown in the settings window; null when they are kept in memory only.</summary>
    public string? FilePath => file?.Path;

    public bool CheckForUpdatesAtStart
    {
        get => current.CheckForUpdatesAtStart;
        set => Change(current with { CheckForUpdatesAtStart = value });
    }

    /// <summary>The stored choice; it is kept while locked and used again on unlock (#246).</summary>
    public bool OfferBetas
    {
        get => current.OfferBetas;
        set => Change(current with { OfferBetas = value });
    }

    /// <summary>False until an unlock code (or a free-unlock build) gives <see cref="Feature.Pro"/>: only stable releases are offered (#246).</summary>
    public bool BetasUnlocked => entitlements?.IsUnlocked(Feature.Pro) == true;

    /// <summary>Whether the update check offers betas: <see cref="OfferBetas"/> while <see cref="BetasUnlocked"/>.</summary>
    public bool BetasOffered => BetasUnlocked && OfferBetas;

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
    public Branding? Branding => Settings.Branding.Read(current.CompanyName, logo, BrandingUnlocked);

    /// <summary>The logo 128 pixels high for the settings window, or null when there is no logo.</summary>
    public string? LogoPreview => logo?.Original == null ? null : logo.Png(128);

    /// <summary>Why the last chosen image was not used, or null.</summary>
    public string? LogoMessage
    {
        get => logoMessage;
        private set => SetField(ref logoMessage, value);
    }

    /// <summary>
    /// Makes <paramref name="path"/> the logo, cropped as <paramref name="crop"/> says or else the whole image; a refused
    /// or failed import keeps the old one and says why.
    /// </summary>
    public void ImportLogo(string path, LogoCrop? crop = null) => ChangeLogo(() => logo?.Import(path, crop));

    /// <summary>The crop window for a new image, starting with the whole image; null, and a message, when it cannot be used.</summary>
    public LogoCropViewModel? StartCrop(string path)
    {
        try
        {
            var (png, aspect) = CompanyLogo.Preview(path);
            LogoMessage = null;
            return new LogoCropViewModel(png, aspect, LogoCrop.Contain(aspect, 1));
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            LogoMessage = e.Message;
        }
        return null;
    }

    /// <summary>The crop window for the current logo at its saved crop; null when there is no logo or it cannot be read.</summary>
    public LogoCropViewModel? AdjustCrop()
    {
        if (logo?.Original is not { } original || StartCrop(original) is not { } crop)
        {
            return null;
        }
        return logo.Crop is { } saved ? new LogoCropViewModel(crop.Png, crop.Aspect, saved) : crop;
    }

    /// <summary>Crops the current logo again; a failed write keeps the old one and says why.</summary>
    public void Recrop(LogoCrop crop) => ChangeLogo(() => logo?.Recrop(crop));

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

    public string? LicenceEmail => current.LicenceEmail;

    public string? LicenceCode => current.LicenceCode;

    /// <summary>Saves the unlock code pair, or forgets it with nulls; only <see cref="Licensing.CodeEntitlements"/> calls this.</summary>
    public void Licence(string? email, string? code) => Change(current with { LicenceEmail = email, LicenceCode = code });

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
        && a.LicenceEmail == b.LicenceEmail && a.LicenceCode == b.LicenceCode
        && a.CheckedOperatingSystems.SequenceEqual(b.CheckedOperatingSystems) && a.CheckedComputers.SequenceEqual(b.CheckedComputers);
}
