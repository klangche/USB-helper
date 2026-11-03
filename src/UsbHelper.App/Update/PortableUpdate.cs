using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace UsbHelper.App.Update;

/// <summary>
/// Updates the portable app (#123): downloads a release's zip and its SHA-256, verifies the zip, and unzips its
/// USB-helper folder into a new folder USB-helper-&lt;version&gt; next to the running app's folder. The running version
/// is left as it is, for the user to delete. Nothing that failed verification is unzipped.
/// </summary>
public static class PortableUpdate
{
    /// <summary>The app's exe in the release zip (not USB-helper.exe: a 0.5.0 launcher would take that one as itself).</summary>
    public const string ExeName = "UsbHelper.exe";

    /// <summary>The folder in the release zip that holds the app.</summary>
    public const string FolderName = "USB-helper";

    /// <summary>Downloads, verifies and unzips the release next to <paramref name="appFolder"/>; returns its exe.</summary>
    public static async Task<string> DownloadAsync(HttpClient http, Release release, string appFolder, Action<int>? progress, CancellationToken cancel)
    {
        var expected = ParseChecksum(await http.GetByteArrayAsync(release.Checksum, cancel));
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(appFolder)))!;
        // The download and the unzip happen here, next to the target, so the last step is a move on the same drive.
        // Removed whatever happens.
        var staging = Path.Combine(parent, $"{FolderName}-{release.Version}.download");
        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true); // left by an update that was cut off
        }
        Directory.CreateDirectory(staging);
        try
        {
            var zip = Path.Combine(staging, "release.zip");
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
            var unzipped = Path.Combine(staging, "unzipped");
            ZipFile.ExtractToDirectory(zip, unzipped); // refuses entries that point outside the folder
            var folder = Path.Combine(unzipped, FolderName);
            if (!File.Exists(Path.Combine(folder, ExeName)))
            {
                throw new InvalidDataException($"{release.Zip.Segments[^1]} holds no {FolderName}/{ExeName}.");
            }
            var target = FreeFolder(parent, $"{FolderName}-{release.Version}");
            Directory.Move(folder, target);
            return Path.Combine(target, ExeName);
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }

    // A folder the user already has under that name is never touched: "<name> (2)", "<name> (3)", ... instead.
    private static string FreeFolder(string parent, string name)
    {
        var path = Path.Combine(parent, name);
        for (var n = 2; Directory.Exists(path) || File.Exists(path); n++)
        {
            path = Path.Combine(parent, string.Create(CultureInfo.InvariantCulture, $"{name} ({n})"));
        }
        return path;
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
