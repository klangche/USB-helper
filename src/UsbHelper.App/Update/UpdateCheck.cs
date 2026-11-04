using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace UsbHelper.App.Update;

/// <summary>
/// A release the app can update to: its zip, .msi or macOS .dmg (<see cref="UpdateKind"/>), that file's SHA-256 file, and its page on
/// GitHub, which the portable and macOS apps open instead of downloading (#131).
/// </summary>
public sealed record Release(string Version, Uri Download, Uri Checksum, Uri Page);

/// <summary>
/// The newest stable release and the newest beta, each only when newer than this version; the log line; and why the
/// check was skipped, when it was.
/// </summary>
public sealed record UpdateCheckResult(Release? Stable, Release? Beta, string Log, string? SkippedBecause = null);

/// <summary>
/// Asks GitHub Releases (no token) for the newest stable release and the newest beta after every start (#111, #123). It never
/// throws: no network, no answer in time, a GitHub error or an unexpected answer each skip the check, with the reason
/// in the log line.
/// </summary>
public static class UpdateCheck
{
    /// <summary>The newest 30 releases, for the newest beta (and a stable one, if it is among them).</summary>
    public const string ReleasesUrl = "https://api.github.com/repos/klangche/USB-helper/releases?per_page=30";

    /// <summary>The newest stable release, however many betas came after it; 404 while there is none.</summary>
    public const string LatestUrl = "https://api.github.com/repos/klangche/USB-helper/releases/latest";

    // A release's page is this plus its tag.
    private const string ReleasePages = "https://github.com/klangche/USB-helper/releases/tag/";

    /// <param name="kind">Which asset a release must have to count: the zip, the .msi for an .msi install, or the .dmg on macOS.</param>
    public static async Task<UpdateCheckResult> FindAsync(HttpClient http, string current, TimeSpan timeout, UpdateKind kind = UpdateKind.Portable)
    {
        using var cancel = new CancellationTokenSource(timeout);
        try
        {
            // Both at once, within the one timeout.
            var list = Get(http, ReleasesUrl, current, cancel.Token);
            var latest = Get(http, LatestUrl, current, cancel.Token);
            using var releases = await list ?? throw new HttpRequestException("GitHub has no release list");
            using var stable = await latest;
            var all = releases.RootElement.EnumerateArray().ToList();
            var stables = stable == null ? all : [.. all, stable.RootElement];
            return Compare(Newest(stables, prerelease: false, kind), Newest(all, prerelease: true, kind), current);
        }
        catch (GitHubException e)
        {
            return Skipped(e.Message);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            return Skipped($"no answer within {timeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)} s");
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return Skipped(e.Message);
        }
    }

    // The answer as JSON, or null for 404 (no such release).
    private static async Task<JsonDocument?> Get(HttpClient http, string url, string current, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("USB-helper", current));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await http.SendAsync(request, cancel);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        if (!response.IsSuccessStatusCode)
        {
            throw new GitHubException($"GitHub answered {(int)response.StatusCode} {response.StatusCode}");
        }
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancel), cancellationToken: cancel);
    }

    private sealed class GitHubException(string message) : Exception(message);

    private static UpdateCheckResult Skipped(string reason) => new(null, null, $"Update check skipped: {reason}", reason);

    private static UpdateCheckResult Compare((ReleaseVersion, Release)? stable, (ReleaseVersion, Release)? beta, string current)
    {
        var mine = ReleaseVersion.Parse(current);
        var newerStable = stable is (var s, var sr) && s.CompareTo(mine) > 0 ? sr : null;
        var newerBeta = beta is (var b, var br) && b.CompareTo(mine) > 0 ? br : null;
        var log = (newerStable, newerBeta) switch
        {
            (null, null) => $"Update check: {current} is up to date (newest with a SHA-256: stable {stable?.Item2.Version ?? "none"}, beta {beta?.Item2.Version ?? "none"})",
            (not null, null) => $"Update check: stable {newerStable.Version} is newer than {current}",
            (null, not null) => $"Update check: beta {newerBeta.Version} is newer than {current}",
            _ => $"Update check: stable {newerStable!.Version} and beta {newerBeta!.Version} are newer than {current}",
        };
        return new UpdateCheckResult(newerStable, newerBeta, log);
    }

    // The highest stable or beta version that is published (not a draft) and has its zip, .msi or .dmg, and that file's checksum.
    private static (ReleaseVersion, Release)? Newest(IEnumerable<JsonElement> releases, bool prerelease, UpdateKind kind)
    {
        (ReleaseVersion, Release)? newest = null;
        foreach (var release in releases)
        {
            if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean() != prerelease
                || ReleaseVersion.Parse(release.GetProperty("tag_name").GetString() ?? "") is not { } version)
            {
                continue;
            }
            var fileName = kind.AssetName(version.ToString());
            var assets = release.GetProperty("assets").EnumerateArray()
                .ToDictionary(asset => asset.GetProperty("name").GetString() ?? "", asset => asset.GetProperty("browser_download_url").GetString() ?? "");
            if (assets.TryGetValue(fileName, out var file) && assets.TryGetValue(fileName + ".sha256", out var checksum)
                && (newest is null || version.CompareTo(newest.Value.Item1) > 0))
            {
                var page = new Uri($"{ReleasePages}{Uri.EscapeDataString(release.GetProperty("tag_name").GetString()!)}");
                newest = (version, new Release(version.ToString(), new Uri(file), new Uri(checksum), page));
            }
        }
        return newest;
    }
}
