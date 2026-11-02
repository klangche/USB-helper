using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace UsbHelper.Launcher;

/// <summary>
/// Puts a chosen release's launcher next to the running one (#111): download the zip and its SHA-256, verify, write
/// the new launcher as &lt;exe&gt;.new, then swap. Nothing that failed verification is written as an exe.
/// </summary>
public static class UpdateInstaller
{
    private const string LauncherName = "USB-helper.exe";

    /// <summary>Downloads and verifies the release; returns the new launcher, written as &lt;exe&gt;.new.</summary>
    public static async Task<string> DownloadAsync(HttpClient http, Release release, string exe, Action<int>? progress, CancellationToken cancel)
    {
        var expected = ParseChecksum(await http.GetByteArrayAsync(release.Checksum, cancel));
        var zip = exe + ".download";
        var downloaded = exe + ".new";
        try
        {
            using (var response = await http.GetAsync(release.Zip, HttpCompletionOption.ResponseHeadersRead, cancel))
            {
                response.EnsureSuccessStatusCode();
                await Copy(response, zip, progress, cancel);
            }
            await using (var file = File.OpenRead(zip))
            {
                if (!(await SHA256.HashDataAsync(file, cancel)).AsSpan().SequenceEqual(expected))
                {
                    throw new InvalidDataException($"{release.Zip.Segments[^1]} does not match its SHA-256.");
                }
            }
            using (var archive = ZipFile.OpenRead(zip))
            {
                var launcher = archive.Entries.FirstOrDefault(entry => entry.Name == LauncherName)
                    ?? throw new InvalidDataException($"{release.Zip.Segments[^1]} holds no {LauncherName}.");
                launcher.ExtractToFile(downloaded, overwrite: true);
            }
            return downloaded;
        }
        catch
        {
            File.Delete(downloaded);
            throw;
        }
        finally
        {
            File.Delete(zip);
        }
    }

    /// <summary>
    /// Moves the running launcher aside as &lt;exe&gt;.old (Windows allows renaming a running exe, not deleting it) and
    /// the new one into its place. If the second move fails, the old launcher is put back.
    /// </summary>
    public static void Replace(string exe, string downloaded)
    {
        var old = exe + ".old";
        File.Move(exe, old, overwrite: true);
        try
        {
            File.Move(downloaded, exe);
        }
        catch
        {
            File.Move(old, exe);
            throw;
        }
    }

    /// <summary>Removes the launcher an update left as &lt;exe&gt;.old, once it no longer runs.</summary>
    public static void RemoveOld(string exe) => File.Delete(exe + ".old");

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
