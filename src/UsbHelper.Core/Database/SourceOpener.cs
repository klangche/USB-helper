using System.Security.Cryptography;

namespace UsbHelper.Core.Database;

/// <summary>Where a source entry opens (#333).</summary>
public enum SourceTarget
{
    /// <summary>The app's own PDF viewer, with <see cref="SourceOpening.Path"/>.</summary>
    Viewer,

    /// <summary>The computer's browser, with <see cref="SourceOpening.Url"/>.</summary>
    Browser,

    /// <summary>A PDF without a public download that is not cached yet: the user opens a copy they have.</summary>
    LocalFile,
}

/// <summary>Why a source entry opens as it does, for a message next to it (#333).</summary>
public enum SourceNotice
{
    None,

    /// <summary>The PDF's fingerprint differs from the one the guide was written with.</summary>
    Changed,

    /// <summary>The download failed, so the address opens in the browser.</summary>
    DownloadFailed,

    /// <summary>What was downloaded is not a PDF, so the address opens in the browser.</summary>
    NotPdf,

    /// <summary>A local file does not match the source's fingerprint.</summary>
    LocalMismatch,
}

/// <summary>How to show one source entry: the target, the file or address, the page, and why.</summary>
public sealed record SourceOpening(SourceTarget Target, string? Path, Uri? Url, int? Page, SourceNotice Notice, string? Reason = null);

/// <summary>
/// Opens a source entry (#333). A link opens in the browser. A PDF opens in the viewer from <c>sources-cache/</c> next to
/// settings.json when a copy is there; otherwise it is downloaded on that click only and checked: it must start with
/// <c>%PDF</c>, and its size and SHA-256 must match when the guide gives them. Nothing is fetched in the background.
/// </summary>
/// <param name="cacheFolder"><c>sources-cache/</c>.</param>
public sealed class SourceOpener(string cacheFolder, HttpClient http)
{
    /// <summary>The most a source PDF may be: larger downloads are refused.</summary>
    public const long MaxBytes = 100L * 1024 * 1024;

    /// <summary><c>sources-cache</c> next to <paramref name="settingsPath"/> (settings.json).</summary>
    public static string NextTo(string settingsPath) => Path.Combine(Path.GetDirectoryName(settingsPath)!, "sources-cache");

    public string Folder { get; } = cacheFolder;

    /// <summary>Where <paramref name="file"/> is cached: by its fingerprint, else by a hash of its address.</summary>
    public string CachePath(SourceFile file) =>
        Path.Combine(Folder, $"{file.Sha256 ?? Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(file.Url!.ToString())))}.pdf");

    /// <param name="page">The page to open at: a quote's page, when known.</param>
    public async Task<SourceOpening> OpenAsync(SourceFile file, int? page, CancellationToken cancel)
    {
        if (!file.IsPdf)
        {
            return new SourceOpening(SourceTarget.Browser, null, file.Url, null, SourceNotice.None);
        }
        var cached = CachePath(file);
        if (File.Exists(cached))
        {
            return new SourceOpening(SourceTarget.Viewer, cached, file.Url, page, Matches(file, await File.ReadAllBytesAsync(cached, cancel)) ? SourceNotice.None : SourceNotice.Changed);
        }
        if (file.Url == null)
        {
            return new SourceOpening(SourceTarget.LocalFile, null, null, page, SourceNotice.None);
        }
        byte[] bytes;
        try
        {
            using var response = await http.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, cancel);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxBytes)
            {
                throw new HttpRequestException($"larger than {MaxBytes / 1024 / 1024} MB");
            }
            bytes = await ReadLimitedAsync(response, cancel);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            return new SourceOpening(SourceTarget.Browser, null, file.Url, page, SourceNotice.DownloadFailed, e.Message);
        }
        if (!IsPdf(bytes))
        {
            return new SourceOpening(SourceTarget.Browser, null, file.Url, page, SourceNotice.NotPdf);
        }
        Store(file, bytes);
        return new SourceOpening(SourceTarget.Viewer, CachePath(file), file.Url, page, Matches(file, bytes) ? SourceNotice.None : SourceNotice.Changed);
    }

    /// <summary>
    /// "Open local file…" for a PDF the user has (#333): checked against the source's fingerprint and kept in the cache.
    /// A file that is not a PDF or does not match is not kept.
    /// </summary>
    public SourceOpening OpenLocal(SourceFile file, string path, int? page)
    {
        var bytes = File.ReadAllBytes(path);
        if (!IsPdf(bytes))
        {
            return new SourceOpening(SourceTarget.LocalFile, null, null, page, SourceNotice.NotPdf);
        }
        if (!Matches(file, bytes))
        {
            return new SourceOpening(SourceTarget.LocalFile, null, null, page, SourceNotice.LocalMismatch);
        }
        Store(file, bytes);
        return new SourceOpening(SourceTarget.Viewer, CachePath(file), file.Url, page, SourceNotice.None);
    }

    // The body up to MaxBytes, also when the server sends no Content-Length.
    private static async Task<byte[]> ReadLimitedAsync(HttpResponseMessage response, CancellationToken cancel)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancel);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancel)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
            {
                throw new HttpRequestException($"larger than {MaxBytes / 1024 / 1024} MB");
            }
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    /// <summary>The file starts with <c>%PDF</c>.</summary>
    public static bool IsPdf(ReadOnlySpan<byte> bytes) => bytes.StartsWith("%PDF"u8);

    // The size and SHA-256 the guide gives, where it gives them.
    private static bool Matches(SourceFile file, byte[] bytes) =>
        (file.Size == null || file.Size == bytes.LongLength)
        && (file.Sha256 == null || file.Sha256 == Convert.ToHexStringLower(SHA256.HashData(bytes)));

    // Written whole to a temporary name first, so a half-written file is never opened.
    private void Store(SourceFile file, byte[] bytes)
    {
        Directory.CreateDirectory(Folder);
        var target = CachePath(file);
        File.WriteAllBytes(target + ".tmp", bytes);
        File.Move(target + ".tmp", target, overwrite: true);
    }
}
