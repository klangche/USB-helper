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
public sealed record Branding(string? CompanyName, string? LogoOriginal, IReadOnlyDictionary<int, string> LogoPngs, string? LogoSvg)
{
    /// <summary>The branding stored in <paramref name="companyName"/> and <paramref name="logo"/>; see <see cref="Branding"/>.</summary>
    public static Branding? Read(string companyName, CompanyLogo? logo, bool unlocked)
    {
        var original = logo?.Original;
        if (!unlocked || (companyName.Length == 0 && original == null))
        {
            return null;
        }
        var pngs = original == null ? new Dictionary<int, string>() : CompanyLogo.Heights.ToDictionary(h => h, logo!.Png);
        return new Branding(companyName.Length == 0 ? null : companyName, original, pngs, logo?.Svg);
    }
}
