using SkiaSharp;
using UsbHelper.App.Settings;
using UsbHelper.App.Tests.Licensing;
using UsbHelper.App.ViewModels;
using UsbHelper.Core.Diagnostics;

namespace UsbHelper.App.Tests.Settings;

/// <summary>The settings window (#126): every change is written at once; a failed write is logged, not thrown.</summary>
public sealed class SettingsViewModelTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("usbhelper-settings-vm-tests-").FullName;
    private readonly EventLog log = new();

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private string FilePath => Path.Combine(folder, "USB-helper", "settings.json");

    [Fact]
    public void EachChangeIsWrittenAtOnce()
    {
        var file = new SettingsFile(FilePath);
        var settings = SettingsViewModel.Load(file, log);

        settings.CheckForUpdatesAtStart = false;
        Assert.False(file.Load(log).CheckForUpdatesAtStart);

        settings.OfferBetas = false;
        Assert.False(file.Load(log).OfferBetas);

        settings.RememberCheckedProfiles = true;
        Assert.True(file.Load(log).RememberCheckedProfiles);
    }

    [Fact]
    public void ABrokenFileIsOnlyReplacedWhenTheUserChangesASetting()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "{ broken");

        var settings = SettingsViewModel.Load(new SettingsFile(FilePath), log);
        settings.CheckedProfiles([], []); // Remember is off (the default): nothing to keep, nothing written.

        Assert.Equal("{ broken", File.ReadAllText(FilePath));

        settings.OfferBetas = false;

        Assert.False(new SettingsFile(FilePath).Load(log).OfferBetas);
    }

    [Fact]
    public void AFailedWriteIsLoggedAndTheSettingStillApplies()
    {
        Directory.CreateDirectory(FilePath); // A folder where the file should be: every write fails.
        var settings = SettingsViewModel.Load(new SettingsFile(FilePath), log);

        settings.OfferBetas = false;

        Assert.False(settings.OfferBetas);
        Assert.StartsWith($"Settings could not be saved to {FilePath}: ", log.Entries[^1].Text);
    }

    [Fact]
    public void TheCompanyNameIsTrimmedAndSaved()
    {
        var file = new SettingsFile(FilePath);
        var settings = SettingsViewModel.Load(file, log);
        var changed = new List<string?>();
        settings.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        settings.CompanyName = "  Acme AB ";

        Assert.Equal("Acme AB", settings.CompanyName);
        Assert.Equal("Acme AB", file.Load(log).CompanyName);
        Assert.Contains(nameof(SettingsViewModel.CompanyName), changed);
    }

    [Fact]
    public void TheLogoIsKeptInBrandingNextToSettingsJson()
    {
        var settings = SettingsViewModel.Load(new SettingsFile(FilePath), log);
        Assert.Null(settings.LogoPreview);

        settings.ImportLogo(TestImages.HalfRed(folder, "logo.png", SKEncodedImageFormat.Png));

        Assert.Equal(Path.Combine(folder, "USB-helper", "branding", "logo-128.png"), settings.LogoPreview);
        Assert.Null(settings.LogoMessage);
    }

    [Fact]
    public void ARefusedLogoShowsAMessageAndKeepsTheOldOne()
    {
        var settings = SettingsViewModel.Load(new SettingsFile(FilePath), log);
        settings.ImportLogo(TestImages.HalfRed(folder, "logo.png", SKEncodedImageFormat.Png));
        var text = Path.Combine(folder, "text.png");
        File.WriteAllText(text, "not an image");

        settings.ImportLogo(text);

        Assert.False(string.IsNullOrEmpty(settings.LogoMessage));
        Assert.True(File.Exists(settings.LogoPreview));

        settings.ImportLogo(TestImages.HalfRed(folder, "logo.webp", SKEncodedImageFormat.Webp));

        Assert.Null(settings.LogoMessage); // A good image clears the message.
    }

    [Fact]
    public void RemoveLogoDeletesIt()
    {
        var settings = SettingsViewModel.Load(new SettingsFile(FilePath), log);
        settings.ImportLogo(TestImages.HalfRed(folder, "logo.png", SKEncodedImageFormat.Png));

        settings.RemoveLogo();

        Assert.Null(settings.LogoPreview);
        Assert.False(Directory.Exists(Path.Combine(folder, "USB-helper", "branding")));
    }

    [Fact]
    public void ACropWindowStartsWithTheWholeImageAndOnlySaveChangesTheLogo()
    {
        var settings = SettingsViewModel.Load(new SettingsFile(FilePath), log);
        settings.ImportLogo(TestImages.HalfRed(folder, "previous.png", SKEncodedImageFormat.Png));
        var before = File.ReadAllBytes(settings.LogoPreview!);
        var next = TestImages.Blank(folder, "tall.png", 100, 400);

        var crop = settings.StartCrop(next)!;

        Assert.Equal(0.25, crop.Aspect);
        Assert.Equal(LogoCrop.Contain(100, 400), crop.Crop);
        Assert.Equal(before, File.ReadAllBytes(settings.LogoPreview!)); // Cancel: nothing changed.

        crop.Zoom = 2;
        settings.ImportLogo(next, crop.Crop);

        Assert.Equal(crop.Crop, new CompanyLogo(Path.Combine(folder, "USB-helper", "branding")).Crop);
        Assert.NotEqual(before, File.ReadAllBytes(settings.LogoPreview!));
    }

    [Fact]
    public void LoadGivesALogoFromBeforeTheCropToolItsDefaultCrop()
    {
        SettingsViewModel.Load(new SettingsFile(FilePath), log).ImportLogo(TestImages.HalfRed(folder, "logo.png", SKEncodedImageFormat.Png));
        var stored = Path.Combine(folder, "USB-helper", "branding");
        File.Delete(Path.Combine(stored, "crop.json"));

        SettingsViewModel.Load(new SettingsFile(FilePath), log);

        Assert.Equal(LogoCrop.Contain(2, 1), new CompanyLogo(stored).Crop);
    }

    [Fact]
    public void AnImageThatCannotBeUsedOpensNoCropWindowAndSaysWhy()
    {
        var settings = SettingsViewModel.Load(new SettingsFile(FilePath), log);
        var text = Path.Combine(folder, "text.png");
        File.WriteAllText(text, "not an image");

        Assert.Null(settings.StartCrop(text));
        Assert.False(string.IsNullOrEmpty(settings.LogoMessage));
    }

    [Fact]
    public void AdjustCropReopensTheSavedCropAndSavesANewOne()
    {
        var settings = SettingsViewModel.Load(new SettingsFile(FilePath), log);
        Assert.Null(settings.AdjustCrop()); // No logo, nothing to adjust.
        var saved = new LogoCrop(0.25, 0.5, 2);
        settings.ImportLogo(TestImages.HalfRed(folder, "logo.png", SKEncodedImageFormat.Png), saved);

        var crop = settings.AdjustCrop()!;

        Assert.Equal(saved, crop.Crop);
        Assert.Equal(2, crop.Aspect);
        var changed = new List<string?>();
        settings.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        crop.Fit();
        settings.Recrop(crop.Crop);

        Assert.Equal(LogoCrop.Contain(2, 1), settings.AdjustCrop()!.Crop);
        Assert.Contains(nameof(SettingsViewModel.LogoPreview), changed);
    }

    [Fact]
    public void BrandingHoldsTheNameAndTheLogoFilesOrIsNoneWhenEmpty()
    {
        var settings = SettingsViewModel.Load(new SettingsFile(FilePath), log);
        var entitlements = new FakeEntitlements();
        entitlements.SetPro(true);
        settings.Gate(entitlements);
        Assert.Null(settings.Branding);

        settings.CompanyName = "Acme AB";
        Assert.Equal("Acme AB", settings.Branding!.CompanyName);
        Assert.Null(settings.Branding.LogoOriginal);
        Assert.Empty(settings.Branding.LogoPngs);

        settings.CompanyName = " ";
        settings.ImportLogo(TestImages.HalfRed(folder, "logo.png", SKEncodedImageFormat.Png));
        var branding = settings.Branding!;
        var stored = Path.Combine(folder, "USB-helper", "branding");
        Assert.Null(branding.CompanyName);
        Assert.Equal(Path.Combine(stored, "logo-original.png"), branding.LogoOriginal);
        Assert.Equal([32, 64, 128, 256, 512], branding.LogoPngs.Keys.Order());
        Assert.Equal(Path.Combine(stored, "logo-512.png"), branding.LogoPngs[512]);
        Assert.Null(branding.LogoSvg);

        File.WriteAllText(Path.Combine(folder, "logo.svg"), """<svg xmlns="http://www.w3.org/2000/svg" width="20" height="10" />""");
        settings.ImportLogo(Path.Combine(folder, "logo.svg"));
        Assert.Equal(Path.Combine(stored, "logo.svg"), settings.Branding!.LogoSvg);

        settings.RemoveLogo();
        Assert.Null(settings.Branding);
    }

    [Fact]
    public void BrandingIsLockedWithoutEntitlements()
    {
        var settings = SettingsViewModel.Load(new SettingsFile(FilePath), log);
        settings.CompanyName = "Acme AB";

        Assert.False(settings.BrandingUnlocked);
        Assert.Null(settings.Branding);
    }

    [Fact]
    public void LockedBrandingIsNoneButTheStoredNameAndLogoAreKept()
    {
        var file = new SettingsFile(FilePath);
        var settings = SettingsViewModel.Load(file, log);
        settings.Gate(new FakeEntitlements());
        settings.CompanyName = "Acme AB";
        settings.ImportLogo(TestImages.HalfRed(folder, "logo.png", SKEncodedImageFormat.Png));

        Assert.False(settings.BrandingUnlocked);
        Assert.Null(settings.Branding);
        Assert.Equal("Acme AB", file.Load(log).CompanyName);
        Assert.NotNull(settings.LogoPreview);
    }

    [Fact]
    public void UnlockingAtRuntimeBringsBrandingBackAndRemovingTheCodeLocksItAgain()
    {
        var entitlements = new FakeEntitlements();
        var settings = SettingsViewModel.Load(new SettingsFile(FilePath), log);
        settings.Gate(entitlements);
        settings.CompanyName = "Acme AB";
        settings.ImportLogo(TestImages.HalfRed(folder, "logo.png", SKEncodedImageFormat.Png));
        var changed = new List<string?>();
        settings.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        entitlements.SetPro(true);
        Assert.True(settings.BrandingUnlocked);
        Assert.Equal("Acme AB", settings.Branding!.CompanyName);
        Assert.NotNull(settings.Branding.LogoOriginal);
        Assert.Equal([nameof(SettingsViewModel.BrandingUnlocked), nameof(SettingsViewModel.Branding)], changed);

        changed.Clear();
        entitlements.SetPro(false);
        Assert.False(settings.BrandingUnlocked);
        Assert.Null(settings.Branding);
        Assert.Equal([nameof(SettingsViewModel.BrandingUnlocked), nameof(SettingsViewModel.Branding)], changed);
    }
}
