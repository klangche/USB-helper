namespace UsbHelper.App.Settings;

/// <summary>
/// The company branding for reports (#228): the one place later steps read it. Get it from
/// <see cref="ViewModels.SettingsViewModel.Branding"/>; it is null when branding is locked, or when there is no name
/// and no logo.
/// </summary>
/// <param name="CompanyName">The trimmed name, or null when none is set.</param>
/// <param name="LogoOriginal">The imported logo file (<c>logo-original.svg|png|webp</c>), or null when there is no logo.</param>
/// <param name="LogoPngs">
/// The cropped logo as PNG by height in pixels (32, 64, 128, 256, 512), always three times as wide (#241); empty when
/// there is no logo.
/// </param>
/// <param name="LogoSvg">The cropped 3:1 logo as SVG when the original is one, else null.</param>
/// <param name="Luminance">The uploaded logo's luminance (<see cref="LogoContrast.Luminance(string)"/>), or null when unknown.</param>
/// <param name="Light">The light theme's own logo (#340), uploaded or inverted, or null.</param>
/// <param name="Dark">The dark theme's own logo (#340), uploaded or inverted, or null.</param>
public sealed record Branding(string? CompanyName, string? LogoOriginal, IReadOnlyDictionary<int, string> LogoPngs, string? LogoSvg,
    double? Luminance = null, ThemeLogo? Light = null, ThemeLogo? Dark = null)
{
    /// <summary>
    /// The logo shown on <paramref name="theme"/> (#340): that theme's own logo; else the uploaded logo when its contrast
    /// there is 3:1 or more; else null, and the company name is shown instead, never an invisible logo.
    /// </summary>
    public ThemeLogo? Logo(LogoTheme theme)
    {
        if ((theme == LogoTheme.Light ? Light : Dark) is { } own)
        {
            return own;
        }
        return LogoOriginal != null && LogoContrast.Visible(Luminance, theme) ? new ThemeLogo(LogoPngs, LogoSvg) : null;
    }

    /// <summary>True when there is an uploaded logo that is too weak on <paramref name="theme"/>, so Settings offers a fix.</summary>
    public bool Weak(LogoTheme theme) => LogoOriginal != null && !LogoContrast.Visible(Luminance, theme);

    /// <summary>The branding stored in <paramref name="companyName"/> and <paramref name="logo"/>; see <see cref="Branding"/>.</summary>
    /// <param name="themes">The theme logos (#340), or null for none.</param>
    /// <param name="luminance">
    /// Gives the uploaded logo's luminance, read once by the caller (null when it has no visible pixel); when not given it
    /// is read from its 128-pixel PNG.
    /// </param>
    public static Branding? Read(string companyName, CompanyLogo? logo, bool unlocked, ThemeLogos? themes = null, Func<double?>? luminance = null)
    {
        var original = logo?.Original;
        if (!unlocked || (companyName.Length == 0 && original == null))
        {
            return null;
        }
        var pngs = original == null ? new Dictionary<int, string>() : PngsOf(logo!);
        return new Branding(companyName.Length == 0 ? null : companyName, original, pngs, logo?.Svg,
            original == null ? null : luminance != null ? luminance() : LogoContrast.Luminance(logo!.Png(128)),
            original == null ? null : ThemeLogo.Of(themes?.Light), original == null ? null : ThemeLogo.Of(themes?.Dark));
    }

    internal static Dictionary<int, string> PngsOf(CompanyLogo logo) => CompanyLogo.Heights.ToDictionary(h => h, logo.Png);
}

/// <summary>A logo to draw (#340): its PNGs by height and, when it is vector, its SVG.</summary>
public sealed record ThemeLogo(IReadOnlyDictionary<int, string> Pngs, string? Svg)
{
    /// <summary>The logo stored in <paramref name="logo"/>, or null when there is none.</summary>
    public static ThemeLogo? Of(CompanyLogo? logo) => logo?.Original == null ? null : new ThemeLogo(Branding.PngsOf(logo), logo.Svg);
}

/// <summary>
/// The two theme logos (#340), each a <see cref="CompanyLogo"/> folder next to the uploaded one: <c>branding-light</c> and
/// <c>branding-dark</c>. Removed when the uploaded logo is replaced, recropped or removed, since they are made from it.
/// </summary>
/// <param name="Light">The light theme's logo folder.</param>
/// <param name="Dark">The dark theme's logo folder.</param>
public sealed record ThemeLogos(CompanyLogo Light, CompanyLogo Dark)
{
    /// <summary>The logo folders of <paramref name="uploaded"/>.</summary>
    public static ThemeLogos Next(CompanyLogo uploaded) =>
        new(new CompanyLogo(uploaded.Folder + "-light"), new CompanyLogo(uploaded.Folder + "-dark"));

    /// <summary>The folder of <paramref name="theme"/>.</summary>
    public CompanyLogo For(LogoTheme theme) => theme == LogoTheme.Light ? Light : Dark;
}
