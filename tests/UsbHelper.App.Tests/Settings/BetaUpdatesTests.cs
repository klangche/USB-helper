using System.Net;
using System.Text;
using System.Xml.Linq;
using UsbHelper.App.Licensing;
using UsbHelper.App.Tests.Licensing;
using UsbHelper.App.Tests.Update;
using UsbHelper.App.Update;
using UsbHelper.App.ViewModels;
using UsbHelper.Core.Diagnostics;

namespace UsbHelper.App.Tests.Settings;

/// <summary>Beta updates only for unlocked users (#246); locked users are offered stable releases only.</summary>
public sealed class BetaUpdatesTests
{
    private readonly EventLog log = new();

    private static string ReleaseJson(string tag, bool prerelease)
    {
        var zip = $"usb-helper-{tag[1..]}-windows-x64.zip";
        var url = $"https://github.com/klangche/USB-helper/releases/download/{tag}/{zip}";
        return $$"""
            {"tag_name":"{{tag}}","draft":false,"prerelease":{{(prerelease ? "true" : "false")}},"assets":[
              {"name":"{{zip}}","browser_download_url":"{{url}}"},{"name":"{{zip}}.sha256","browser_download_url":"{{url}}.sha256"}]}
            """;
    }

    // GitHub with stable 0.6.0 and beta 0.7.0-beta.1.
    private static FakeHttp GitHub() => new((request, _) =>
    {
        var json = request.RequestUri!.ToString() == UpdateCheck.LatestUrl ? ReleaseJson("v0.6.0", false)
            : $"[{ReleaseJson("v0.7.0-beta.1", true)},{ReleaseJson("v0.6.0", false)}]";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    });

    private UpdateViewModel Update(SettingsViewModel settings, string current = "0.5.0") =>
        new(GitHub().Client(), current, UpdateKind.Portable, Path.GetTempPath(), _ => { }, _ => { }, log, TimeSpan.FromSeconds(2), settings);

    private static SettingsViewModel Gated(bool unlocked, bool offerBetas)
    {
        var entitlements = new FakeEntitlements();
        entitlements.SetPro(unlocked);
        var settings = new SettingsViewModel { OfferBetas = offerBetas };
        settings.Gate(entitlements);
        return settings;
    }

    [Fact]
    public async Task LockedWithBetasOnOffersOnlyStable()
    {
        var update = Update(Gated(unlocked: false, offerBetas: true));

        await update.CheckAtStartAsync();

        Assert.Equal("Update to stable 0.6.0", update.StableText);
        Assert.Null(update.BetaText);
        Assert.Contains(log.Entries, entry => entry.Text == "Beta updates need an unlock code: the beta is not offered");
    }

    [Fact]
    public async Task UnlockedWithBetasOnOffersTheBeta()
    {
        var update = Update(Gated(unlocked: true, offerBetas: true));

        await update.CheckAtStartAsync();

        Assert.Equal("Update to stable 0.6.0", update.StableText);
        Assert.NotNull(update.BetaText);
    }

    [Fact]
    public async Task UnlockedWithBetasOffOffersOnlyStable()
    {
        var update = Update(Gated(unlocked: true, offerBetas: false));

        await update.CheckAtStartAsync();

        Assert.Equal("Update to stable 0.6.0", update.StableText);
        Assert.Null(update.BetaText);
    }

    [Fact]
    public async Task TheMenuCheckFollowsTheSameRule()
    {
        var update = Update(Gated(unlocked: false, offerBetas: true));

        update.CheckNow.Execute(null);
        await update.Checking;

        Assert.True(update.IsOffered);
        Assert.Null(update.BetaText);
    }

    [Fact]
    public async Task ALockedUserOnABetaIsOfferedOnlyANewerStableRelease()
    {
        var update = Update(Gated(unlocked: false, offerBetas: true), current: "0.7.0-beta.1");

        await update.CheckAsync(fromMenu: true);

        Assert.Null(update.StableText); // 0.6.0 is older than the beta it runs: no downgrade
        Assert.Null(update.BetaText);
        Assert.Equal("USB-helper 0.7.0-beta.1 is up to date.", update.Message);
    }

    [Fact]
    public async Task UnlockingAndLockingAtRuntimeIsReEvaluatedAtTheNextCheck()
    {
        var entitlements = new FakeEntitlements();
        var settings = new SettingsViewModel();
        settings.Gate(entitlements);
        var update = Update(settings);
        await update.CheckAsync(fromMenu: true);
        Assert.Null(update.BetaText);

        entitlements.SetPro(true);
        await update.CheckAsync(fromMenu: true);
        Assert.NotNull(update.BetaText);

        entitlements.SetPro(false);
        await update.CheckAsync(fromMenu: true);
        Assert.Null(update.BetaText);
    }

    [Fact]
    public async Task AFreeUnlockBuildCountsAsUnlocked()
    {
        var settings = new SettingsViewModel();
        settings.Gate(new CodeEntitlements(new Dictionary<int, byte[]>(), settings, log, freeUnlock: true));
        var update = Update(settings);

        await update.CheckAtStartAsync();

        Assert.True(settings.BetasUnlocked);
        Assert.NotNull(update.BetaText);
    }

    [Fact]
    public void TheCheckboxIsEnabledOnlyWhileUnlockedAndTheStoredChoiceIsKept()
    {
        var entitlements = new FakeEntitlements();
        var settings = new SettingsViewModel { OfferBetas = false };
        settings.Gate(entitlements);
        var changed = new List<string?>();
        settings.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.False(settings.BetasUnlocked);

        entitlements.SetPro(true);
        Assert.True(settings.BetasUnlocked);
        Assert.Contains(nameof(SettingsViewModel.BetasUnlocked), changed);
        Assert.False(settings.OfferBetas);

        changed.Clear();
        entitlements.SetPro(false);
        Assert.False(settings.BetasUnlocked);
        Assert.Contains(nameof(SettingsViewModel.BetasUnlocked), changed);
        Assert.False(settings.OfferBetas);
    }

    [Fact]
    public void TheCheckboxShowsUntickedWhileLockedAndTheStoredChoiceAgainOnUnlock()
    {
        var entitlements = new FakeEntitlements();
        var settings = new SettingsViewModel();
        settings.Gate(entitlements);
        var changed = new List<string?>();
        settings.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.True(settings.OfferBetas);
        Assert.False(settings.BetasOffered); // shows what happens: no betas

        entitlements.SetPro(true);
        Assert.True(settings.BetasOffered);
        Assert.Contains(nameof(SettingsViewModel.BetasOffered), changed);

        settings.BetasOffered = false;
        Assert.False(settings.OfferBetas);
        Assert.False(settings.BetasOffered);
        settings.BetasOffered = true;
        Assert.True(settings.OfferBetas);

        changed.Clear();
        entitlements.SetPro(false);
        Assert.False(settings.BetasOffered);
        Assert.Contains(nameof(SettingsViewModel.BetasOffered), changed);
        Assert.True(settings.OfferBetas);
    }

    [Fact]
    public void TickingWhileUnlockedIsSaved()
    {
        var folder = Directory.CreateTempSubdirectory("usbhelper-betas-tests-").FullName;
        try
        {
            var file = new UsbHelper.App.Settings.SettingsFile(Path.Combine(folder, "settings.json"));
            var settings = SettingsViewModel.Load(file, log);
            var entitlements = new FakeEntitlements();
            entitlements.SetPro(true);
            settings.Gate(entitlements);
            var changed = new List<string?>();
            settings.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            settings.BetasOffered = false;

            Assert.False(file.Load(log).OfferBetas);
            Assert.Contains(nameof(SettingsViewModel.BetasOffered), changed);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void WhileLockedTheShownStateCannotChangeTheStoredChoice()
    {
        var settings = new SettingsViewModel();
        settings.Gate(new FakeEntitlements());

        settings.BetasOffered = false;

        Assert.True(settings.OfferBetas);
    }

    [Fact]
    public void WithoutEntitlementsBetasAreLocked() => Assert.False(new SettingsViewModel().BetasUnlocked);

    [Fact]
    public void TheSettingsWindowDisablesTheCheckboxWhileLockedAndLinksToTheUnlockPopup()
    {
        XNamespace avalonia = "https://github.com/avaloniaui";
        var window = Xaml("SettingsWindow.axaml");
        var checkbox = window.Descendants(avalonia + "CheckBox").Single(element => (string?)element.Attribute("Content") == "{x:Static res:Strings.SettingsOfferBetas}");
        Assert.Equal("{Binding BetasOffered}", (string?)checkbox.Attribute("IsChecked"));
        Assert.Equal("{Binding BetasUnlocked}", (string?)checkbox.Attribute("IsEnabled"));

        var link = window.Descendants(avalonia + "HyperlinkButton").Single(element => (string?)element.Attribute("Content") == "{x:Static res:Strings.UnlockBetasNeeded}");
        Assert.Equal("OpenUnlock", (string?)link.Attribute("Click"));
        Assert.Equal("{Binding !BetasUnlocked}", (string?)link.Attribute("IsVisible"));
        Assert.Equal("Enter a code to unlock beta updates", Resources.Strings.UnlockBetasNeeded);
    }

    private static XElement Xaml(string file)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "UsbHelper.App", file);
            if (File.Exists(candidate))
            {
                return XElement.Load(candidate);
            }
        }
        throw new FileNotFoundException(file);
    }
}
