using SkiaSharp;
using UsbHelper.App.Licensing;
using UsbHelper.App.Settings;
using UsbHelper.App.ViewModels;
using UsbHelper.Core.Diagnostics;
using static UsbHelper.App.Tests.Licensing.UnlockCodeTests;

namespace UsbHelper.App.Tests.Settings;

/// <summary>The company logo on the dark and the light theme (#340): contrast, inverting and which logo is shown.</summary>
public sealed class LogoContrastTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("usbhelper-logo-contrast-tests-").FullName;
    private readonly EventLog log = new();

    public void Dispose() => Directory.Delete(folder, recursive: true);

    // A 300 x 100 PNG: transparent, with a mark of colour in the middle third, and a faint grey haze elsewhere when asked.
    private string Mark(string name, SKColor colour, bool haze = false)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(300, 100, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.Erase(haze ? new SKColor(128, 128, 128, 10) : SKColors.Transparent);
        for (var y = 25; y < 75; y++)
        {
            for (var x = 100; x < 200; x++)
            {
                bitmap.SetPixel(x, y, colour);
            }
        }
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    [Fact]
    public void ABlackLogoIsWeakOnDarkAndFineOnLight()
    {
        var luminance = LogoContrast.Luminance(Mark("black.png", SKColors.Black));

        Assert.Equal(0, luminance!.Value, 3);
        Assert.False(LogoContrast.Visible(luminance, LogoTheme.Dark));
        Assert.True(LogoContrast.Visible(luminance, LogoTheme.Light));
        Assert.Equal(21, LogoContrast.Ratio(luminance.Value, LogoContrast.Background(LogoTheme.Light)), 1);
    }

    [Fact]
    public void AWhiteLogoIsWeakOnLight()
    {
        var luminance = LogoContrast.Luminance(Mark("white.png", SKColors.White));

        Assert.False(LogoContrast.Visible(luminance, LogoTheme.Light));
        Assert.True(LogoContrast.Visible(luminance, LogoTheme.Dark));
    }

    [Fact]
    public void ATransparentLogoIsJudgedByItsVisibleMarkOnly()
    {
        // The haze (alpha 10) covers far more pixels than the mark, but is below the threshold, so it does not count.
        Assert.Equal(0, LogoContrast.Luminance(Mark("hazy.png", SKColors.Black, haze: true))!.Value, 3);
        Assert.Null(LogoContrast.Luminance(Mark("empty.png", SKColors.Transparent)));
    }

    [Fact]
    public void InvertingMakesABlackMarkWhiteAndKeepsTheAlpha()
    {
        var png = LogoContrast.Inverted(Mark("black.png", new SKColor(0, 0, 0, 200)))!;

        using var bitmap = SKBitmap.Decode(SKCodec.Create(new MemoryStream(png)),
            new SKImageInfo(300, 100, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        Assert.Equal(new SKColor(255, 255, 255, 200), bitmap.GetPixel(150, 50));
        Assert.Equal(0, bitmap.GetPixel(10, 10).Alpha);
    }

    [Fact]
    public void TheThemeLogoComesFirstThenAVisibleUploadThenTheName()
    {
        var pngs = new Dictionary<int, string> { [64] = "logo-64.png" };
        var dark = new ThemeLogo(new Dictionary<int, string> { [64] = "dark-64.png" }, null);
        var black = new Branding("Acme AB", "logo-original.png", pngs, null, Luminance: 0);

        Assert.Same(dark, (black with { Dark = dark }).Logo(LogoTheme.Dark)); // The dark logo is used on dark.
        Assert.Null(black.Logo(LogoTheme.Dark)); // None set and weak: the company name.
        Assert.Equal("logo-64.png", black.Logo(LogoTheme.Light)!.Pngs[64]); // Fine: the uploaded logo.
        Assert.True(black.Weak(LogoTheme.Dark));
        Assert.False(black.Weak(LogoTheme.Light));
        Assert.Null(new Branding("Acme AB", null, new Dictionary<int, string>(), null).Logo(LogoTheme.Light)); // No logo.
    }

    // The app with an unlocked settings model and a black logo uploaded.
    private (SettingsViewModel Settings, MainViewModel Main) BlackLogo()
    {
        var settings = SettingsViewModel.Load(new SettingsFile(Path.Combine(folder, "USB-helper", "settings.json")), log);
        var entitlements = new CodeEntitlements(TestKeys, settings, log);
        var main = new MainViewModel(new Core.Detection.ComputerIdentity(null, null), log: log, settings: settings, entitlements: entitlements);
        entitlements.Unlock(TestEmail, TestCode);
        settings.CompanyName = "Acme AB";
        settings.ImportLogo(Mark("black.png", SKColors.Black));
        return (settings, main);
    }

    [Fact]
    public void TheOfferAppearsOnlyOnTheThemeWhereTheLogoIsWeak()
    {
        var (settings, _) = BlackLogo();

        Assert.True(settings.ThemeOffer(LogoTheme.Dark).Offered);
        Assert.False(settings.ThemeOffer(LogoTheme.Light).Offered);
        Assert.Equal("This logo is hard to see on the dark theme", settings.ThemeOffer(LogoTheme.Dark).HardToSee);

        settings.ImportLogo(Mark("white.png", SKColors.White));
        Assert.False(settings.ThemeOffer(LogoTheme.Dark).Offered);
        Assert.True(settings.ThemeOffer(LogoTheme.Light).Offered);
    }

    [Fact]
    public void InvertItPreviewsThenSavesTheCopyAndUsesItAndRemovingFallsBackToTheName()
    {
        var (settings, main) = BlackLogo();
        main.Theme = LogoTheme.Dark;
        Assert.Null(main.BrandingLogo);
        Assert.Equal("Acme AB", main.BrandingName);
        var offer = settings.ThemeOffer(LogoTheme.Dark);

        offer.Invert();
        Assert.True(offer.Previewing);
        Assert.False(Directory.Exists(Path.Combine(folder, "USB-helper", "branding-dark"))); // Nothing kept yet.
        offer.CancelInvert();
        Assert.False(offer.Previewing);

        offer.Invert();
        offer.KeepInverted();
        var dark = Path.Combine(folder, "USB-helper", "branding-dark", "logo-64.png");
        Assert.Equal(dark, main.BrandingLogo);
        Assert.Null(main.BrandingName);
        Assert.True(offer.HasOwnLogo);
        Assert.Equal(1, LogoContrast.Luminance(dark)!.Value, 3); // White now.
        main.Theme = LogoTheme.Light;
        Assert.Equal(Path.Combine(folder, "USB-helper", "branding", "logo-64.png"), main.BrandingLogo); // Light is unchanged.
        main.Theme = LogoTheme.Dark;

        offer.Remove();
        Assert.Null(main.BrandingLogo);
        Assert.Equal("Acme AB", main.BrandingName);
        Assert.False(offer.HasOwnLogo);
    }

    [Fact]
    public void AThemeChangeUpdatesTheTopBar()
    {
        var (_, main) = BlackLogo();
        var changed = new List<string?>();
        main.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.NotNull(main.BrandingLogo); // Light at start: black on white is fine.

        main.Theme = LogoTheme.Dark;

        Assert.Contains(nameof(MainViewModel.BrandingLogo), changed);
        Assert.Contains(nameof(MainViewModel.BrandingName), changed);
        Assert.Null(main.BrandingLogo);
        Assert.Equal("Acme AB", main.BrandingName);
        Assert.Equal(LogoTheme.Dark, MainWindow.ThemeOf(Avalonia.Styling.ThemeVariant.Dark));
        Assert.Equal(LogoTheme.Light, MainWindow.ThemeOf(Avalonia.Styling.ThemeVariant.Light));
    }

    [Fact]
    public void ANewUploadDropsTheThemeLogosMadeFromTheOldOne()
    {
        var (settings, _) = BlackLogo();
        settings.ThemeOffer(LogoTheme.Dark).Invert();
        settings.ThemeOffer(LogoTheme.Dark).KeepInverted();

        settings.ImportLogo(Mark("black2.png", SKColors.Black));

        Assert.False(settings.ThemeOffer(LogoTheme.Dark).HasOwnLogo);
        Assert.False(Directory.Exists(Path.Combine(folder, "USB-helper", "branding-dark")));
    }

    [Fact]
    public void TheReportTakesTheLightThemeChoice()
    {
        var pngs = new Dictionary<int, string> { [512] = "logo-512.png" };
        var light = new ThemeLogo(new Dictionary<int, string> { [512] = "light-512.png" }, null);

        Assert.Null(MainViewModel.ReportLogo(new Branding("Acme AB", "logo-original.png", pngs, null, Luminance: 1)));
        Assert.Equal("light-512.png", MainViewModel.ReportLogo(new Branding("Acme AB", "logo-original.png", pngs, null, Luminance: 1, Light: light)));
        Assert.Equal("logo-512.png", MainViewModel.ReportLogo(new Branding("Acme AB", "logo-original.png", pngs, null, Luminance: 0)));
    }
}
