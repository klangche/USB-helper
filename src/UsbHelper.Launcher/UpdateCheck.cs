using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace UsbHelper.Launcher;

/// <summary>A release the launcher can update to: its zip and the zip's SHA-256 file.</summary>
public sealed record Release(string Version, Uri Zip, Uri Checksum);

/// <summary>The newest stable release and the newest beta, each only when newer than this version; and the log line.</summary>
public sealed record UpdateCheckResult(Release? Stable, Release? Beta, string Log);

/// <summary>
/// Asks GitHub Releases (no token) for the newest stable release and the newest beta on every launch (#111). It never
/// throws: no network, no answer in time, a GitHub error or an unexpected answer each skip the check, with the reason
/// in the log line.
/// </summary>
public static class UpdateCheck
{
    public const string ReleasesUrl = "https://api.github.com/repos/klangche/USB-helper/releases?per_page=30";

    public static async Task<UpdateCheckResult> FindAsync(HttpClient http, string current, TimeSpan timeout)
    {
        using var cancel = new CancellationTokenSource(timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("USB-helper", current));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await http.SendAsync(request, cancel.Token);
            if (!response.IsSuccessStatusCode)
            {
                return Skipped($"GitHub answered {(int)response.StatusCode} {response.StatusCode}");
            }
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancel.Token), cancellationToken: cancel.Token);
            return Compare(Newest(json.RootElement, prerelease: false), Newest(json.RootElement, prerelease: true), current);
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

    private static UpdateCheckResult Skipped(string reason) => new(null, null, $"Update check skipped: {reason}");

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

    // The highest version of one kind that is published (not a draft) and has its zip and checksum.
    private static (ReleaseVersion, Release)? Newest(JsonElement releases, bool prerelease)
    {
        (ReleaseVersion, Release)? newest = null;
        foreach (var release in releases.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean() != prerelease
                || ReleaseVersion.Parse(release.GetProperty("tag_name").GetString() ?? "") is not { } version)
            {
                continue;
            }
            var zipName = $"usb-helper-{version}-windows-x64.zip";
            var assets = release.GetProperty("assets").EnumerateArray()
                .ToDictionary(asset => asset.GetProperty("name").GetString() ?? "", asset => asset.GetProperty("browser_download_url").GetString() ?? "");
            if (assets.TryGetValue(zipName, out var zip) && assets.TryGetValue(zipName + ".sha256", out var checksum)
                && (newest is null || version.CompareTo(newest.Value.Item1) > 0))
            {
                newest = (version, new Release(version.ToString(), new Uri(zip), new Uri(checksum)));
            }
        }
        return newest;
    }
}
