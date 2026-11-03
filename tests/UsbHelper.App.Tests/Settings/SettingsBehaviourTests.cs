using System.Net;
using System.Text;
using UsbHelper.App.Tests.Update;
using UsbHelper.App.Update;
using UsbHelper.App.ViewModels;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;

namespace UsbHelper.App.Tests.Settings;

/// <summary>Each setting (#126) changes the behaviour it names.</summary>
public sealed class SettingsBehaviourTests
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

    // GitHub with stable 0.6.0 and beta 0.7.0-beta.1; counts the requests.
    private static FakeHttp GitHub(List<string> requests) => new((request, _) =>
    {
        var url = request.RequestUri!.ToString();
        requests.Add(url);
        var json = url == UpdateCheck.LatestUrl ? ReleaseJson("v0.6.0", false)
            : $"[{ReleaseJson("v0.7.0-beta.1", true)},{ReleaseJson("v0.6.0", false)}]";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    });

    private UpdateViewModel Update(FakeHttp http, SettingsViewModel settings, string current = "0.5.0") =>
        new(http.Client(), current, Path.GetTempPath(), _ => { }, log, TimeSpan.FromSeconds(2), settings);

    [Fact]
    public async Task CheckForUpdatesAtStartOffAsksGitHubNothingAtStart()
    {
        var requests = new List<string>();
        var update = Update(GitHub(requests), new SettingsViewModel { CheckForUpdatesAtStart = false });

        await update.CheckAtStartAsync();

        Assert.Empty(requests);
        Assert.False(update.IsOffered);
        Assert.Equal("Update check at start is off in the settings", log.Entries[^1].Text);
    }

    [Fact]
    public async Task CheckForUpdatesAtStartOnChecks()
    {
        var requests = new List<string>();
        var update = Update(GitHub(requests), new SettingsViewModel());

        await update.CheckAtStartAsync();

        Assert.NotEmpty(requests);
        Assert.True(update.IsOffered);
    }

    [Fact]
    public async Task TheMenuChecksEvenWhenTheCheckAtStartIsOff()
    {
        var requests = new List<string>();
        var update = Update(GitHub(requests), new SettingsViewModel { CheckForUpdatesAtStart = false });

        update.CheckNow.Execute(null);
        await update.Checking;

        Assert.NotEmpty(requests);
        Assert.True(update.IsOffered);
    }

    [Fact]
    public async Task WithoutBetasOnlyTheStableReleaseIsOffered()
    {
        var update = Update(GitHub([]), new SettingsViewModel { OfferBetas = false });

        await update.CheckAtStartAsync();

        Assert.True(update.IsOffered);
        Assert.Equal("Update to stable 0.6.0", update.StableText);
        Assert.Null(update.BetaText);
    }

    [Fact]
    public async Task WithoutBetasANewerBetaAloneIsNoUpdate()
    {
        var update = Update(GitHub([]), new SettingsViewModel { OfferBetas = false }, current: "0.6.0");

        await update.CheckAtStartAsync();
        Assert.False(update.IsOffered);

        await update.CheckAsync(fromMenu: true);
        Assert.Equal("USB-helper 0.6.0 is up to date.", update.Message);
        Assert.Null(update.BetaText);
    }

    [Fact]
    public async Task TurningBetasOffLaterHidesTheBetaChoice()
    {
        var settings = new SettingsViewModel();
        var update = Update(GitHub([]), settings);
        await update.CheckAtStartAsync();
        Assert.NotNull(update.BetaText);

        settings.OfferBetas = false;
        await update.CheckAsync(fromMenu: true);

        Assert.Null(update.BetaText);
    }

    private static readonly ComputerIdentity Identity = new("LENOVO", "21K5", Platform: "p");

    private static MainViewModel Window(SettingsViewModel settings) =>
        new(DatabaseFolder.Load("LENOVO", "21K5", "verified"), [], Identity, settings: settings);

    [Fact]
    public void RememberedProfilesAreCheckedAtTheNextStart()
    {
        var settings = new SettingsViewModel { RememberCheckedProfiles = true };
        var first = Window(settings);
        Assert.True(first.OperatingSystems.Single().IsChecked); // The detected platform, as always on a first start.

        first.OperatingSystems.Single().IsChecked = false;
        first.Computers.Single().IsChecked = true;

        Assert.Empty(settings.CheckedOperatingSystems);
        Assert.Equal([first.Computers.Single().Profile.Id], settings.CheckedComputers);

        var next = Window(settings);
        Assert.False(next.OperatingSystems.Single().IsChecked);
        Assert.True(next.Computers.Single().IsChecked);
    }

    [Fact]
    public void WithoutRememberEachStartChecksTheDetectedPlatformOnly()
    {
        var settings = new SettingsViewModel();
        var first = Window(settings);
        first.OperatingSystems.Single().IsChecked = false;
        first.Computers.Single().IsChecked = true;

        Assert.Empty(settings.CheckedComputers);

        var next = Window(settings);
        Assert.True(next.OperatingSystems.Single().IsChecked);
        Assert.False(next.Computers.Single().IsChecked);
    }

    [Fact]
    public void TurningRememberOnKeepsWhatIsCheckedNowAndTurningItOffForgetsIt()
    {
        var settings = new SettingsViewModel();
        var window = Window(settings);
        window.Computers.Single().IsChecked = true;

        settings.RememberCheckedProfiles = true;

        Assert.Equal(["p"], settings.CheckedOperatingSystems);
        Assert.Equal([window.Computers.Single().Profile.Id], settings.CheckedComputers);

        settings.RememberCheckedProfiles = false;

        Assert.Empty(settings.CheckedOperatingSystems);
        Assert.Empty(settings.CheckedComputers);
    }
}
