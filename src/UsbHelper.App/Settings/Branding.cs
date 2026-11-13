namespace UsbHelper.App.Settings;

/// <summary>
/// The company branding for reports (#228): the one place later steps read it. Get it from
/// <see cref="ViewModels.SettingsViewModel.Branding"/>; it is null when branding is locked, or when there is no name
/// and no logo.
/// </summary>
/// <param name="CompanyName">The trimmed name, or null when none is set.</param>
/// <param name="LogoOriginal">The imported logo file (<c>logo-original.svg|png|webp</c>), or null when there is no logo.</param>
/// <param name="LogoPngs">The logo as PNG by height in pixels (32, 64, 128, 256, 512); empty when there is no logo.</param>
public sealed record Branding(string? CompanyName, string? LogoOriginal, IReadOnlyDictionary<int, string> LogoPngs)
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
        return new Branding(companyName.Length == 0 ? null : companyName, original, pngs);
    }
}
