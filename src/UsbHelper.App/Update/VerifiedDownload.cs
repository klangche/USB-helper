using System.Security.Cryptography;
using System.Text;

namespace UsbHelper.App.Update;

/// <summary>
/// Downloads a release's zip or .msi and checks it against the release's SHA-256 file (#111, #123, #124). A file that
/// does not match is deleted, so nothing unverified is ever unzipped or run.
/// </summary>
public static class VerifiedDownload
{
    /// <summary>Writes the release's download to <paramref name="path"/> and verifies it; reports percent done.</summary>
    public static async Task DownloadAsync(HttpClient http, Release release, string path, Action<int>? progress, CancellationToken cancel)
    {
        var expected = ParseChecksum(await http.GetByteArrayAsync(release.Checksum, cancel));
        try
        {
            using (var response = await http.GetAsync(release.Download, HttpCompletionOption.ResponseHeadersRead, cancel))
            {
                response.EnsureSuccessStatusCode();
                await Copy(response, path, progress, cancel);
            }
            await using (var file = File.OpenRead(path))
            {
                if (!(await SHA256.HashDataAsync(file, cancel)).AsSpan().SequenceEqual(expected))
                {
                    throw new InvalidDataException($"{release.Download.Segments[^1]} does not match its SHA-256.");
                }
            }
        }
        catch
        {
            File.Delete(path);
            throw;
        }
    }

    // sha256sum format: "<64 hex digits>  <file name>".
    private static byte[] ParseChecksum(byte[] file)
    {
        var hex = Encoding.ASCII.GetString(file).Split(' ', 2)[0].Trim();
        try
        {
            var hash = Convert.FromHexString(hex);
            if (hash.Length == SHA256.HashSizeInBytes)
            {
                return hash;
            }
        }
        catch (FormatException)
        {
        }
        throw new InvalidDataException("The release's SHA-256 file cannot be read.");
    }

    private static async Task Copy(HttpResponseMessage response, string path, Action<int>? progress, CancellationToken cancel)
    {
        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancel);
        await using var target = File.Create(path);
        var buffer = new byte[81920];
        long done = 0;
        var reported = -1;
        int read;
        while ((read = await source.ReadAsync(buffer, cancel)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancel);
            done += read;
            var percent = total > 0 ? (int)(done * 100 / total.Value) : 0;
            if (percent != reported)
            {
                progress?.Invoke(reported = percent);
            }
        }
        if (reported != 100)
        {
            progress?.Invoke(100);
        }
    }
}
