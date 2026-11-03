using System.Net;
using System.Text;
using UsbHelper.App.Update;

namespace UsbHelper.App.Tests.Update;

/// <summary>The update check against a fake GitHub Releases feed (#111).</summary>
public class UpdateCheckTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    // One release as the GitHub API lists it, with the zip (and the .msi, if asked) and their checksums unless left out.
    private static string Release(string tag, bool prerelease, bool checksum = true, bool draft = false, bool msi = false)
    {
        var version = tag.TrimStart('v');
        var assets = new List<string>();
        foreach (var file in msi ? new[] { "zip", "msi" } : ["zip"])
        {
            var name = $"usb-helper-{version}-windows-x64.{file}";
            assets.Add($$"""{"name":"{{name}}","browser_download_url":"https://github.com/klangche/USB-helper/releases/download/{{tag}}/{{name}}"}""");
            if (checksum)
            {
                assets.Add($$"""{"name":"{{name}}.sha256","browser_download_url":"https://github.com/klangche/USB-helper/releases/download/{{tag}}/{{name}}.sha256"}""");
            }
        }
        return $$"""{"tag_name":"{{tag}}","draft":{{(draft ? "true" : "false")}},"prerelease":{{(prerelease ? "true" : "false")}},"assets":[{{string.Join(',', assets)}}]}""";
    }

    // The release list as GitHub gives it, newest first, and its "latest" release: the newest published stable one.
    private static FakeHttp Feed(params string[] releases) =>
        Feed(releases, releases.FirstOrDefault(r => r.Contains("\"draft\":false,\"prerelease\":false", StringComparison.Ordinal)));

    private static FakeHttp Feed(string[] releases, string? latest) => new((request, _) =>
    {
        Assert.Contains("USB-helper", request.Headers.UserAgent.ToString()); // GitHub refuses requests without one
        var body = request.RequestUri!.ToString() switch
        {
            UpdateCheck.ReleasesUrl => $"[{string.Join(',', releases)}]",
            UpdateCheck.LatestUrl => latest,
            var other => throw new InvalidOperationException(other),
        };
        return Task.FromResult(body == null
            ? new HttpResponseMessage(HttpStatusCode.NotFound) // GitHub's answer when there is no stable release
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    });

    private static readonly string[] Releases =
    [
        Release("v0.7.0-beta.2", prerelease: true),
        Release("v0.7.0-beta.1", prerelease: true),
        Release("v0.6.0", prerelease: false),
        Release("v0.6.0-beta.3", prerelease: true),
        Release("v0.5.0", prerelease: false),
    ];

    [Fact]
    public async Task ANewerStableOnly()
    {
        var feed = Feed(Release("v0.6.0", prerelease: false), Release("v0.6.0-beta.3", prerelease: true));

        var result = await UpdateCheck.FindAsync(feed.Client(), "0.6.0-beta.3", Timeout);

        Assert.Equal("0.6.0", result.Stable!.Version);
        Assert.Equal("https://github.com/klangche/USB-helper/releases/download/v0.6.0/usb-helper-0.6.0-windows-x64.zip", result.Stable.Download.ToString());
        Assert.Equal(result.Stable.Download + ".sha256", result.Stable.Checksum.ToString());
        Assert.Null(result.Beta);
        Assert.Equal("Update check: stable 0.6.0 is newer than 0.6.0-beta.3", result.Log);
    }

    [Fact]
    public async Task ANewerBetaOnly()
    {
        var result = await UpdateCheck.FindAsync(Feed(Releases).Client(), "0.6.0", Timeout);

        Assert.Null(result.Stable);
        Assert.Equal("0.7.0-beta.2", result.Beta!.Version);
        Assert.Equal("Update check: beta 0.7.0-beta.2 is newer than 0.6.0", result.Log);
    }

    [Fact]
    public async Task BothNewer()
    {
        var result = await UpdateCheck.FindAsync(Feed(Releases).Client(), "0.5.0", Timeout);

        Assert.Equal("0.6.0", result.Stable!.Version);
        Assert.Equal("0.7.0-beta.2", result.Beta!.Version);
        Assert.Equal("Update check: stable 0.6.0 and beta 0.7.0-beta.2 are newer than 0.5.0", result.Log);
    }

    [Fact]
    public async Task NoneNewer()
    {
        var result = await UpdateCheck.FindAsync(Feed(Releases).Client(), "0.7.0-beta.2", Timeout);

        Assert.Null(result.Stable);
        Assert.Null(result.Beta);
        Assert.Equal("Update check: 0.7.0-beta.2 is up to date (newest with a SHA-256: stable 0.6.0, beta 0.7.0-beta.2)", result.Log);
    }

    [Fact]
    public async Task AStableOlderThanAFullPageOfBetasIsFound()
    {
        // The list holds only the newest 30 releases; after many betas the stable one is no longer in it.
        var betas = Enumerable.Range(1, 30).Reverse().Select(n => Release($"v0.5.0-beta.{n}", prerelease: true)).ToArray();

        var result = await UpdateCheck.FindAsync(Feed(betas, Release("v0.4.0", prerelease: false)).Client(), "0.3.0", Timeout);

        Assert.Equal("0.4.0", result.Stable!.Version);
        Assert.Equal("0.5.0-beta.30", result.Beta!.Version);
    }

    [Fact]
    public async Task NoStableReleaseYet()
    {
        var result = await UpdateCheck.FindAsync(Feed([Release("v0.5.0-beta.1", prerelease: true)], latest: null).Client(), "0.4.0", Timeout);

        Assert.Null(result.Stable);
        Assert.Equal("0.5.0-beta.1", result.Beta!.Version);
    }

    [Fact]
    public async Task ReleasesWithoutAChecksumOrDraftsAreNotOffered()
    {
        // Releases made before the workflow published checksums cannot be verified.
        var feed = Feed(
            Release("v0.8.0", prerelease: false, draft: true),
            Release("v0.7.0", prerelease: false, checksum: false),
            Release("v0.6.0", prerelease: false));

        var result = await UpdateCheck.FindAsync(feed.Client(), "0.5.0", Timeout);

        Assert.Equal("0.6.0", result.Stable!.Version);
    }

    [Fact]
    public async Task TheMsiInstallIsOfferedOnlyReleasesWithAnMsi()
    {
        // Releases before #124 have only the zip.
        var feed = Feed(
            Release("v0.7.0-beta.1", prerelease: true, msi: true),
            Release("v0.6.0", prerelease: false, msi: true),
            Release("v0.5.1", prerelease: false));

        var result = await UpdateCheck.FindAsync(feed.Client(), "0.5.0", Timeout, UpdateKind.Msi);

        Assert.Equal("https://github.com/klangche/USB-helper/releases/download/v0.6.0/usb-helper-0.6.0-windows-x64.msi", result.Stable!.Download.ToString());
        Assert.Equal(result.Stable.Download + ".sha256", result.Stable.Checksum.ToString());
        Assert.Equal("0.7.0-beta.1", result.Beta!.Version);
        Assert.EndsWith(".msi", result.Beta.Download.ToString());
    }

    [Fact]
    public async Task AnMsiInstallIsNotOfferedAZipOnlyRelease()
    {
        var result = await UpdateCheck.FindAsync(Feed(Release("v0.6.0", prerelease: false)).Client(), "0.5.0", Timeout, UpdateKind.Msi);

        Assert.Null(result.Stable);
    }

    [Theory]
    [InlineData("Programs/USB-helper", UpdateKind.Msi)]
    [InlineData("Programs/USB-helper/", UpdateKind.Msi)] // AppContext.BaseDirectory ends with a separator
    [InlineData("Programs/usb-helper/", UpdateKind.Msi)] // Windows paths ignore case
    [InlineData("Programs/USB-helper-0.6.0/", UpdateKind.Portable)]
    [InlineData("USB-helper/", UpdateKind.Portable)]
    public void TheMsiInstallFolderMeansAnMsiInstall(string appFolder, UpdateKind expected)
    {
        var localAppData = Path.Combine(Path.GetTempPath(), "LocalAppData");

        var kind = UpdateKinds.For(Path.Combine(localAppData, appFolder.Replace('/', Path.DirectorySeparatorChar)), localAppData);

        Assert.Equal(expected, kind);
    }

    [Fact]
    public async Task OfflineIsSkipped()
    {
        var offline = new FakeHttp((_, _) => throw new HttpRequestException("No such host is known."));

        var result = await UpdateCheck.FindAsync(offline.Client(), "0.6.0", Timeout);

        Assert.Null(result.Stable);
        Assert.Null(result.Beta);
        Assert.Equal("Update check skipped: No such host is known.", result.Log);
    }

    [Fact]
    public async Task NoAnswerInTimeIsSkipped()
    {
        var silent = new FakeHttp(async (_, cancel) =>
        {
            await Task.Delay(Timeout * 10, cancel);
            throw new InvalidOperationException("not reached");
        });

        var waited = System.Diagnostics.Stopwatch.StartNew();
        var result = await UpdateCheck.FindAsync(silent.Client(), "0.6.0", TimeSpan.FromMilliseconds(200));

        Assert.True(waited.Elapsed < TimeSpan.FromSeconds(2), waited.Elapsed.ToString());
        Assert.Null(result.Stable);
        Assert.Equal("Update check skipped: no answer within 0.2 s", result.Log);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "Update check skipped: GitHub answered 403 Forbidden")] // the rate limit
    [InlineData(HttpStatusCode.InternalServerError, "Update check skipped: GitHub answered 500 InternalServerError")]
    public async Task AGitHubErrorIsSkipped(HttpStatusCode status, string log)
    {
        var error = new FakeHttp((_, _) => Task.FromResult(new HttpResponseMessage(status)));

        var result = await UpdateCheck.FindAsync(error.Client(), "0.6.0", Timeout);

        Assert.Null(result.Stable);
        Assert.Null(result.Beta);
        Assert.Equal(log, result.Log);
    }

    [Fact]
    public async Task AnUnexpectedAnswerIsSkipped()
    {
        var html = new FakeHttp((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>") }));

        var result = await UpdateCheck.FindAsync(html.Client(), "0.6.0", Timeout);

        Assert.Null(result.Stable);
        Assert.StartsWith("Update check skipped: ", result.Log);
    }
}
