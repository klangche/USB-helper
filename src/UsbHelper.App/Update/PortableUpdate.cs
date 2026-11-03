using System.Globalization;
using System.IO.Compression;

namespace UsbHelper.App.Update;

/// <summary>
/// Updates the portable app (#123): downloads a release's zip and its SHA-256, verifies the zip, and unzips its
/// USB-helper folder into a new folder USB-helper-&lt;version&gt; next to the running app's top folder. The running version
/// is left as it is, for the user to delete. Nothing that failed verification is unzipped. The folder holds
/// USB-helper.exe, which starts app\UsbHelper.exe (#130).
/// </summary>
public static class PortableUpdate
{
    /// <summary>The exe at the top of the release zip's folder: it starts the app (#130).</summary>
    public const string StarterName = "USB-helper.exe";

    /// <summary>The app's exe, in the app folder below the starter.</summary>
    public static readonly string AppExe = Path.Combine("app", "UsbHelper.exe");

    /// <summary>The folder in the release zip that holds the app.</summary>
    public const string FolderName = "USB-helper";

    /// <summary>
    /// Downloads, verifies and unzips the release next to <paramref name="topFolder"/> (the folder of USB-helper.exe);
    /// returns the new version's USB-helper.exe.
    /// </summary>
    public static async Task<string> DownloadAsync(HttpClient http, Release release, string topFolder, Action<int>? progress, CancellationToken cancel)
    {
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(topFolder)))!;
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
            foreach (var exe in new[] { StarterName, AppExe })
            {
                if (!File.Exists(Path.Combine(folder, exe)))
                {
                    throw new InvalidDataException($"{release.Download.Segments[^1]} holds no {FolderName}/{exe.Replace('\\', '/')}.");
                }
            }
            var target = FreeFolder(parent, $"{FolderName}-{release.Version}");
            Directory.Move(folder, target);
            return Path.Combine(target, StarterName);
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
