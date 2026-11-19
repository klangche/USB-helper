using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using UsbHelper.Core.Database;

namespace UsbHelper.App.Update;

/// <summary>
/// Asks GitHub (no token) for the newest database files of this app's format (#290), without a manifest: the newest
/// commit that touched <c>v&lt;format&gt;/</c> on the database branch is the version, its commit date says which is newer,
/// and that commit's tree lists the files, which are then fetched from raw.githubusercontent.com at that commit. Every
/// failure throws; <see cref="DatabaseUpdate"/> logs it and keeps the database in use.
/// </summary>
public static class DatabaseCheck
{
    private const string Repository = "klangche/USB-helper";

    // Limits for what is downloaded: the database is about 30 files of a few kilobytes each.
    private const int MaxFiles = 500;
    private const int MaxFileBytes = 1024 * 1024;

    /// <summary>The newest commit that touched the newest format's folder on the database branch.</summary>
    public static readonly string CommitsUrl = CommitsUrlOf(DatabaseFormats.Newest);

    /// <summary>The newest commit that touched <paramref name="format"/>'s folder on the database branch (#334).</summary>
    public static string CommitsUrlOf(int format) =>
        $"https://api.github.com/repos/{Repository}/commits?sha=database&path={DatabaseFormats.Folder(format)}&per_page=1";

    /// <summary>Every file of a commit (its sha appended, then <c>?recursive=1</c>).</summary>
    public const string TreesUrl = $"https://api.github.com/repos/{Repository}/git/trees/";

    /// <summary>A file at a commit: this, the sha, the format's folder and the file's path.</summary>
    public const string RawUrl = $"https://raw.githubusercontent.com/{Repository}/";

    /// <summary>The newest commit of this format's folder, with its committer date.</summary>
    /// <exception cref="HttpRequestException">No network, or GitHub answered with an error (a rate limit too).</exception>
    /// <exception cref="InvalidDataException">GitHub's answer is not what was expected.</exception>
    /// <param name="format">The format whose folder is checked; the newest this app reads when not given.</param>
    public static async Task<DatabaseVersion> NewestAsync(HttpClient http, string appVersion, CancellationToken cancel, int? format = null)
    {
        var folder = DatabaseFormats.Folder(format ?? DatabaseFormats.Newest);
        using var json = await GetJsonAsync(http, CommitsUrlOf(format ?? DatabaseFormats.Newest), appVersion, cancel);
        var commit = json.RootElement.EnumerateArray().FirstOrDefault();
        if (commit.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"the database branch has no {folder}/ folder");
        }
        var sha = commit.GetProperty("sha").GetString();
        var date = commit.GetProperty("commit").GetProperty("committer").GetProperty("date").GetString();
        if (!DatabaseVersion.IsSha(sha) || !DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when))
        {
            throw new InvalidDataException("GitHub's commit has no valid sha or date");
        }
        return new DatabaseVersion(sha!, when, BuiltIn: false);
    }

    /// <summary>
    /// Every <c>.yaml</c> and <c>.yml</c> file of this format's folder at <paramref name="version"/>, by its path inside the
    /// folder, ready for <see cref="HardwareDatabase.Load(IReadOnlyDictionary{string, string})"/>. Files whose name starts
    /// with a dot are left out, as the loader does.
    /// </summary>
    /// <exception cref="HttpRequestException">No network, or GitHub answered with an error.</exception>
    /// <exception cref="InvalidDataException">The tree or a file is not what was expected: too many or too large files, or an unsafe path.</exception>
    /// <param name="format">The format whose folder is downloaded; the newest this app reads when not given.</param>
    public static async Task<Dictionary<string, string>> DownloadAsync(
        HttpClient http, string appVersion, DatabaseVersion version, CancellationToken cancel, int? format = null)
    {
        var folder = DatabaseFormats.Folder(format ?? DatabaseFormats.Newest);
        if (!DatabaseVersion.IsSha(version.Sha))
        {
            throw new InvalidDataException("not a commit sha");
        }
        using var json = await GetJsonAsync(http, $"{TreesUrl}{version.Sha}?recursive=1", appVersion, cancel);
        if (json.RootElement.TryGetProperty("truncated", out var truncated) && truncated.GetBoolean())
        {
            throw new InvalidDataException("GitHub's file list is truncated");
        }
        var paths = new List<string>();
        foreach (var entry in json.RootElement.GetProperty("tree").EnumerateArray())
        {
            var path = entry.GetProperty("path").GetString() ?? "";
            if (entry.GetProperty("type").GetString() == "blob" && path.StartsWith(folder + "/", StringComparison.Ordinal)
                && Path.GetExtension(path) is ".yaml" or ".yml")
            {
                var relative = SafePath(path[(folder.Length + 1)..]);
                if (!relative.Split('/').Any(part => part.StartsWith('.')))
                {
                    paths.Add(relative); // not .template.yaml: the loader leaves it out
                }
            }
        }
        if (paths.Count > MaxFiles)
        {
            throw new InvalidDataException($"{paths.Count} files, more than {MaxFiles}");
        }
        var files = new Dictionary<string, string>();
        foreach (var path in paths)
        {
            var url = $"{RawUrl}{version.Sha}/{folder}/{string.Join('/', path.Split('/').Select(Uri.EscapeDataString))}";
            files[path] = await GetTextAsync(http, url, appVersion, cancel);
        }
        return files;
    }

    /// <summary>
    /// A path from GitHub's tree, checked before it is used as a file name in the cache: relative, '/' between folders,
    /// and no part that is empty, "." or "..", or holds a character a file name cannot have.
    /// </summary>
    /// <exception cref="InvalidDataException">The path is not safe.</exception>
    public static string SafePath(string path)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':']).ToHashSet();
        if (path.Split('/').Any(part => part.Length == 0 || part is "." or ".." || part.Any(invalid.Contains)))
        {
            throw new InvalidDataException($"unsafe file name '{path}'");
        }
        return path;
    }

    private static HttpRequestMessage Request(string url, string appVersion, string accept)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("USB-helper", appVersion));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        return request;
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient http, string url, string appVersion, CancellationToken cancel)
    {
        using var request = Request(url, appVersion, "application/vnd.github+json");
        using var response = await http.SendAsync(request, cancel);
        Check(response);
        try
        {
            return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancel), cancellationToken: cancel);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"GitHub's answer is not JSON ({e.Message})", e);
        }
    }

    // A file's text as UTF-8, read up to the size limit.
    private static async Task<string> GetTextAsync(HttpClient http, string url, string appVersion, CancellationToken cancel)
    {
        using var request = Request(url, appVersion, "text/plain");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
        Check(response);
        await using var stream = await response.Content.ReadAsStreamAsync(cancel);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancel)) > 0)
        {
            if (buffer.Length + read > MaxFileBytes)
            {
                throw new InvalidDataException($"{url} is larger than {MaxFileBytes / 1024} KB");
            }
            buffer.Write(chunk, 0, read);
        }
        // Without a byte order mark, as File.ReadAllText reads the files of a folder.
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length).TrimStart('\uFEFF');
    }

    private static void Check(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"GitHub answered {(int)response.StatusCode} {response.StatusCode}");
        }
    }
}
