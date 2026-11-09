using System.Net;
using System.Security.Cryptography;
using System.Text;
using UsbHelper.App.Update;
using UsbHelper.App.ViewModels;
using UsbHelper.Core.Diagnostics;

namespace UsbHelper.App.Tests.Update;

/// <summary>The update popup (#123, #131): checked after start, never blocking it, with both choices and Skip.</summary>
public sealed class UpdateViewModelTests : IDisposable
{
    private readonly string parent = Directory.CreateTempSubdirectory("usbhelper-update-vm-tests-").FullName;
    private readonly string app;
    private readonly EventLog log = new();
    private readonly List<string> restarted = [];
    private readonly List<Uri> opened = [];

    public UpdateViewModelTests()
    {
        app = Path.Combine(parent, "USB-helper");
        Directory.CreateDirectory(app);
    }

    public void Dispose() => Directory.Delete(parent, recursive: true);

    private static string Download(string tag, string file = "zip") =>
        $"https://github.com/klangche/USB-helper/releases/download/{tag}/usb-helper-{tag[1..]}-windows-x64.{file}";

    private static string ReleaseJson(string tag, bool prerelease) =>
        $$"""
        {"tag_name":"{{tag}}","draft":false,"prerelease":{{(prerelease ? "true" : "false")}},"assets":[
          {"name":"usb-helper-{{tag[1..]}}-windows-x64.zip","browser_download_url":"{{Download(tag)}}"},
          {"name":"usb-helper-{{tag[1..]}}-windows-x64.zip.sha256","browser_download_url":"{{Download(tag)}}.sha256"},
          {"name":"usb-helper-{{tag[1..]}}-windows-x64.msi","browser_download_url":"{{Download(tag, "msi")}}"},
          {"name":"usb-helper-{{tag[1..]}}-windows-x64.msi.sha256","browser_download_url":"{{Download(tag, "msi")}}.sha256"}]}
        """;

    // GitHub with stable 0.6.0 and beta 0.7.0-beta.1, each with its zip; answers once the gate opens.
    private static FakeHttp GitHub(Task? gate = null, bool badChecksum = false)
    {
        var stableZip = Encoding.UTF8.GetBytes("zip 0.6.0");
        var betaZip = Encoding.UTF8.GetBytes("zip 0.7.0-beta.1");
        var files = new Dictionary<string, string>
        {
            [UpdateCheck.ReleasesUrl] = $"[{ReleaseJson("v0.7.0-beta.1", true)},{ReleaseJson("v0.6.0", false)}]",
            [UpdateCheck.LatestUrl] = ReleaseJson("v0.6.0", false),
        };
        var binary = new Dictionary<string, byte[]>
        {
            [Download("v0.6.0")] = stableZip,
            [Download("v0.6.0") + ".sha256"] = Checksum(badChecksum ? betaZip : stableZip),
            [Download("v0.7.0-beta.1")] = betaZip,
            [Download("v0.7.0-beta.1") + ".sha256"] = Checksum(betaZip),
            [Download("v0.6.0", "msi")] = StableMsi,
            [Download("v0.6.0", "msi") + ".sha256"] = Checksum(badChecksum ? betaZip : StableMsi),
        };
        return new FakeHttp(async (request, cancel) =>
        {
            if (gate != null)
            {
                await gate.WaitAsync(cancel);
            }
            var url = request.RequestUri!.ToString();
            return files.TryGetValue(url, out var json)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") }
                : binary.TryGetValue(url, out var bytes)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
        });
    }

    private static readonly byte[] StableMsi = Encoding.UTF8.GetBytes("msi 0.6.0");

    private static byte[] Checksum(byte[] zip) => Encoding.ASCII.GetBytes($"{Convert.ToHexStringLower(SHA256.HashData(zip))}  x.zip\n");

    private UpdateViewModel Model(FakeHttp http, string current = "0.5.0") =>
        new(http.Client(), current, UpdateKind.Portable, app, restarted.Add, opened.Add, log, TimeSpan.FromSeconds(2));

    // An app installed by the .msi (#124): it downloads into this folder and hands the .msi to restart.
    private UpdateViewModel MsiModel(FakeHttp http) =>
        new(http.Client(), "0.5.0", UpdateKind.Msi, Path.Combine(parent, "download"), restarted.Add, opened.Add, log, TimeSpan.FromSeconds(2));

    [Fact]
    public async Task AnMsiInstallDownloadsTheVerifiedMsiAndRunsIt()
    {
        var model = MsiModel(GitHub());
        await model.CheckAsync();

        await model.InstallAsync(stable: true);

        var msi = Path.Combine(parent, "download", "usb-helper-0.6.0-windows-x64.msi");
        Assert.Equal([msi], restarted);
        Assert.Equal("msi 0.6.0", File.ReadAllText(msi));
        Assert.Equal($"Downloaded 0.6.0 to {msi}; installing it", log.Entries[^1].Text);
        // The installed app is never unzipped next to itself.
        Assert.Equal(["USB-helper", "download"], Directory.GetDirectories(parent).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task AnMsiInstallKeepsTheInstalledVersionsMsiAndRemovesOlderOnes()
    {
        // #222: Windows Installer needs the installed version's .msi (0.5.0, running) to remove it during the upgrade and
        // at an uninstall from Apps; deleting it made both ask for a network location. Older ones (about 80 MB) go.
        var download = Path.Combine(parent, "download");
        Directory.CreateDirectory(download);
        File.WriteAllText(Path.Combine(download, "usb-helper-0.4.0-windows-x64.msi"), "msi 0.4.0");
        File.WriteAllText(Path.Combine(download, "usb-helper-0.5.0-windows-x64.msi"), "msi 0.5.0");
        var model = MsiModel(GitHub());
        await model.CheckAsync();

        await model.InstallAsync(stable: true);

        Assert.Equal(["usb-helper-0.5.0-windows-x64.msi", "usb-helper-0.6.0-windows-x64.msi"],
            Directory.GetFiles(download).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal("msi 0.5.0", File.ReadAllText(Path.Combine(download, "usb-helper-0.5.0-windows-x64.msi")));
    }

    [Fact]
    public async Task AnMsiWithABadChecksumIsDeletedAndNotRun()
    {
        var model = MsiModel(GitHub(badChecksum: true));
        await model.CheckAsync();

        await model.InstallAsync(stable: true);

        Assert.Empty(restarted);
        Assert.Empty(Directory.GetFiles(Path.Combine(parent, "download")));
        Assert.StartsWith("The update could not be installed: usb-helper-0.6.0-windows-x64.msi does not match its SHA-256.", model.Status);
    }

    [Fact]
    public async Task TheCheckRunsInTheBackgroundAndShowsNothingUntilItHasAnAnswer()
    {
        var gate = new TaskCompletionSource();
        var model = Model(GitHub(gate.Task));

        var check = model.CheckAsync();

        // The window is up and usable while GitHub has not answered.
        Assert.False(check.IsCompleted);
        Assert.False(model.IsOffered);

        gate.SetResult();
        await check;

        Assert.True(model.IsOffered);
        Assert.Equal("A newer version is available. You have 0.5.0.", model.Message);
        Assert.Equal("Update to stable 0.6.0", model.StableText);
        Assert.Equal("Update to beta 0.7.0-beta.1 (test build)", model.BetaText);
        Assert.Contains("Update check: stable 0.6.0 and beta 0.7.0-beta.1 are newer than 0.5.0", log.Entries.Select(entry => entry.Text));
    }

    [Fact]
    public async Task OnlyTheNewerChoiceIsOffered()
    {
        var model = Model(GitHub(), current: "0.6.0");

        await model.CheckAsync();

        Assert.True(model.IsOffered);
        Assert.Null(model.StableText);
        Assert.Equal("Update to beta 0.7.0-beta.1 (test build)", model.BetaText);
    }

    [Fact]
    public async Task UpToDateShowsNothing()
    {
        var model = Model(GitHub(), current: "0.7.0-beta.1");

        await model.CheckAsync();

        Assert.False(model.IsOffered);
        Assert.StartsWith("Update check: 0.7.0-beta.1 is up to date", log.Entries[^1].Text);
    }

    [Fact]
    public async Task OfflineShowsNothingAndIsLogged()
    {
        var model = Model(new FakeHttp((_, _) => throw new HttpRequestException("No such host is known.")));

        await model.CheckAsync();

        Assert.False(model.IsOffered);
        Assert.Equal("Update check skipped: No such host is known.", log.Entries[^1].Text);
    }

    [Fact]
    public async Task CheckingFromTheMenuWhenUpToDateSaysSo()
    {
        var model = Model(GitHub(), current: "0.7.0-beta.1");

        await model.CheckAsync(fromMenu: true);

        Assert.True(model.IsOffered);
        Assert.Equal("USB-helper 0.7.0-beta.1 is up to date.", model.Message);
        Assert.Null(model.StableText);
        Assert.Null(model.BetaText);
        Assert.Equal("OK", model.CloseText);

        model.Skip.Execute(null);

        Assert.False(model.IsOffered);
    }

    [Fact]
    public async Task ALateStartUpCheckWithNothingNewerKeepsTheMenusNotice()
    {
        // The start-up check's two requests wait at the gate; the menu's check, started meanwhile, answers at once.
        var gate = new TaskCompletionSource();
        var github = GitHub();
        var requests = 0;
        var http = new FakeHttp(async (request, cancel) =>
        {
            if (Interlocked.Increment(ref requests) <= 2)
            {
                await gate.Task.WaitAsync(cancel);
            }
            return await github.Client().SendAsync(new HttpRequestMessage(request.Method, request.RequestUri), cancel);
        });
        var model = Model(http, current: "0.7.0-beta.1");

        var startUp = model.CheckAsync();
        await model.CheckAsync(fromMenu: true);
        gate.SetResult();
        await startUp;

        Assert.True(model.IsOffered);
        Assert.Equal("USB-helper 0.7.0-beta.1 is up to date.", model.Message);
    }

    [Fact]
    public async Task CheckingFromTheMenuOfflineSaysTheCheckFailed()
    {
        var model = Model(new FakeHttp((_, _) => throw new HttpRequestException("No such host is known.")));

        await model.CheckAsync(fromMenu: true);

        Assert.True(model.IsOffered);
        Assert.Equal("The update check failed: No such host is known.", model.Message);
        Assert.Equal("OK", model.CloseText);
    }

    [Fact]
    public async Task CheckingFromTheMenuOffersNewerReleasesAsAtStart()
    {
        var model = Model(GitHub());

        model.CheckNow.Execute(null);
        await model.Checking;

        Assert.True(model.IsOffered);
        Assert.Equal("A newer version is available. You have 0.5.0.", model.Message);
        Assert.Equal("Update to stable 0.6.0", model.StableText);
        Assert.Equal("Skip", model.CloseText);
    }

    [Fact]
    public async Task SkipHidesTheNotice()
    {
        var model = Model(GitHub());
        await model.CheckAsync();

        model.Skip.Execute(null);

        Assert.False(model.IsOffered);
        Assert.Empty(restarted);
        Assert.Equal("Update skipped", log.Entries[^1].Text);
    }

    [Fact]
    public async Task APortableAppOpensTheReleasePageInsteadOfDownloading()
    {
        var github = GitHub();
        var model = Model(github);
        await model.CheckAsync();
        Assert.Equal("Opens the release page in your browser: download the new zip there.", model.Note);

        await model.InstallAsync(stable: false);

        Assert.Equal([new Uri("https://github.com/klangche/USB-helper/releases/tag/v0.7.0-beta.1")], opened);
        Assert.Empty(restarted);
        Assert.DoesNotContain(github.Requests, url => url.ToString().Contains("/releases/download/", StringComparison.Ordinal));
        Assert.False(model.IsOffered); // the popup closes; this version keeps running
        Assert.Equal("Opened the release page of 0.7.0-beta.1", log.Entries[^1].Text);
    }

    [Fact]
    public async Task AMacOsAppOpensTheReleasePageAndExplainsHowToReplaceTheApp()
    {
        var tag = "v0.7.0-beta.1";
        var dmg = $"https://github.com/klangche/USB-helper/releases/download/{tag}/usb-helper-0.7.0-beta.1-macos-universal.dmg";
        var release = $$"""{"tag_name":"{{tag}}","draft":false,"prerelease":true,"assets":[{"name":"usb-helper-0.7.0-beta.1-macos-universal.dmg","browser_download_url":"{{dmg}}"},{"name":"usb-helper-0.7.0-beta.1-macos-universal.dmg.sha256","browser_download_url":"{{dmg}}.sha256"}]}""";
        var github = new FakeHttp((request, _) => Task.FromResult(request.RequestUri!.ToString() switch
        {
            UpdateCheck.ReleasesUrl => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"[{release}]", Encoding.UTF8, "application/json") },
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        }));
        var model = new UpdateViewModel(github.Client(), "0.6.0", UpdateKind.MacOs, app, restarted.Add, opened.Add, log, TimeSpan.FromSeconds(2));
        await model.CheckAsync();
        Assert.Contains("Quit USB-helper", model.Note);
        Assert.Contains("drag USB-helper.app to Applications", model.Note);

        await model.InstallAsync(stable: false);

        Assert.Equal([new Uri("https://github.com/klangche/USB-helper/releases/tag/v0.7.0-beta.1")], opened);
        Assert.Empty(restarted);
        Assert.DoesNotContain(github.Requests, url => url.ToString().Contains("/releases/download/", StringComparison.Ordinal));
        Assert.Empty(Directory.GetFiles(app)); // the running app is never touched
        Assert.False(model.IsOffered);
    }

    [Fact]
    public async Task AnMsiInstallHasNoNote()
    {
        var model = MsiModel(GitHub());
        await model.CheckAsync();

        Assert.Null(model.Note);
    }
}
