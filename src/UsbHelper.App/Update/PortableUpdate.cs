using System.Globalization;
using System.IO.Compression;

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
            await VerifiedDownload.DownloadAsync(http, release, zip, progress, cancel);
            var unzipped = Path.Combine(staging, "unzipped");
            await ZipFile.ExtractToDirectoryAsync(zip, unzipped, cancel); // refuses entries outside the folder; async, so the window stays responsive
            var folder = Path.Combine(unzipped, FolderName);
            if (!File.Exists(Path.Combine(folder, ExeName)))
            {
                throw new InvalidDataException($"{release.Download.Segments[^1]} holds no {FolderName}/{ExeName}.");
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
}
