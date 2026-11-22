using UsbHelper.App.Resources;
using UsbHelper.App.Settings;

namespace UsbHelper.App.ViewModels;

/// <summary>
/// The offer in Settings for a logo that is hard to see on one theme (#340): "Invert it", with a preview before it is
/// kept, or upload a logo for that theme. Once the theme has its own logo, that is shown with a way to remove it.
/// </summary>
public sealed class ThemeLogoViewModel : ObservableObject
{
    private readonly SettingsViewModel settings;
    private byte[]? invertPreview;
    private string? message;

    internal ThemeLogoViewModel(SettingsViewModel settings, LogoTheme theme)
    {
        this.settings = settings;
        Theme = theme;
    }

    public LogoTheme Theme { get; }

    /// <summary>"This logo is hard to see on the dark theme" (or light).</summary>
    public string HardToSee => Theme == LogoTheme.Light ? Strings.LogoHardToSeeLight : Strings.LogoHardToSeeDark;

    /// <summary>"Upload a logo for the dark theme" (or light).</summary>
    public string UploadLabel => Theme == LogoTheme.Light ? Strings.LogoUploadLight : Strings.LogoUploadDark;

    /// <summary>True while the uploaded logo is too weak on this theme, so the offer shows; also when the theme has its own logo.</summary>
    public bool Offered => settings.Branding?.Weak(Theme) == true;

    /// <summary>The theme's own logo 128 pixels high, or null when it has none.</summary>
    public string? OwnLogo => settings.Branding is { } branding && (Theme == LogoTheme.Light ? branding.Light : branding.Dark) is { } own
        ? own.Pngs.GetValueOrDefault(128)
        : null;

    /// <summary>True when the theme has its own logo: then only "Remove" is offered.</summary>
    public bool HasOwnLogo => OwnLogo != null;

    /// <summary>True when the theme has no logo of its own and no inverted copy is being previewed: the two choices show.</summary>
    public bool ShowsChoices => !HasOwnLogo && invertPreview == null;

    /// <summary>The inverted copy shown before it is kept, as PNG; null when none is being previewed.</summary>
    public byte[]? InvertPreview
    {
        get => invertPreview;
        private set
        {
            if (SetField(ref invertPreview, value))
            {
                OnPropertyChanged(nameof(Previewing));
                OnPropertyChanged(nameof(ShowsChoices));
            }
        }
    }

    /// <summary>True while an inverted copy waits for "Keep" or "Cancel".</summary>
    public bool Previewing => invertPreview != null;

    /// <summary>Why the last change was not made, or null.</summary>
    public string? Message
    {
        get => message;
        internal set => SetField(ref message, value);
    }

    /// <summary>"Invert it": makes the inverted copy and shows it; nothing is saved yet.</summary>
    public void Invert()
    {
        InvertPreview = settings.InvertedLogo();
        Message = InvertPreview == null ? Strings.LogoUnreadable : null;
    }

    /// <summary>Saves the previewed copy as this theme's logo.</summary>
    public void KeepInverted()
    {
        if (invertPreview is { } png)
        {
            InvertPreview = null;
            settings.KeepInverted(Theme, png);
        }
    }

    public void CancelInvert() => InvertPreview = null;

    public void Upload(string path, LogoCrop? crop) => settings.ImportThemeLogo(Theme, path, crop);

    public void Remove() => settings.RemoveThemeLogo(Theme);

    // The uploaded logo or a theme logo changed.
    internal void Changed()
    {
        InvertPreview = null;
        OnPropertyChanged(nameof(Offered));
        OnPropertyChanged(nameof(OwnLogo));
        OnPropertyChanged(nameof(HasOwnLogo));
        OnPropertyChanged(nameof(ShowsChoices));
    }
}
