using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using UsbHelper.Launcher.Resources;

namespace UsbHelper.Launcher;

/// <summary>
/// USB-helper.exe (#110): shows the splash at once, unpacks the app it carries on the first start of a version, starts
/// it, closes the splash when the app's window is up, then removes other versions. Meanwhile it checks for a newer
/// stable release or beta (#111) and, if the user picks one, replaces itself with it and starts that instead.
/// </summary>
internal static partial class Program
{
    private const string AppExe = "UsbHelper.App.exe";

    /// <summary>The app writes this variable's text to its log (#87): the update check's result.</summary>
    public const string UpdateLogVariable = "USBHELPER_UPDATE_CHECK";

    private static readonly TimeSpan UpdateCheckTimeout = TimeSpan.FromSeconds(2);

    // A whole update download (about 65 MB): ends one that stalls, leaves time for a slow line.
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(10);

    public static int Main(string[] args) => OperatingSystem.IsWindows() ? Run(args) : 1;

    [SupportedOSPlatform("windows")]
    private static int Run(string[] args)
    {
        // The version given to the build (-p:Version), without the commit the SDK appends after "+".
        var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion.Split('+')[0];
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "USB-helper");
        var exe = Environment.ProcessPath!;
        string? error = null;
        var updated = false;
        try
        {
            UpdateInstaller.RemoveOld(exe); // left by the update that started this launcher
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Still running for a moment, or not ours to delete; the next start tries again.
        }
        using var http = new HttpClient();
        // Runs while the app is unpacked; the app starts once it has answered or timed out.
        var check = UpdateCheck.FindAsync(http, version, UpdateCheckTimeout);

        Splash.Run(
            string.Format(CultureInfo.CurrentCulture, Strings.LauncherVersion, version),
            AppFolder.IsComplete(Path.Combine(root, version)) ? Strings.LauncherStarting : Strings.LauncherUnpacking,
            () =>
            {
                try
                {
                    var folder = AppFolder.Prepare(root, version, OpenPayload);
                    UpdateCheckResult result;
                    try
                    {
                        result = check.GetAwaiter().GetResult();
                    }
                    catch (Exception e)
                    {
                        // The check handles every expected failure; anything else must not stop the start either.
                        result = new UpdateCheckResult(null, null, $"Update check skipped: {e.Message}");
                    }
                    var log = result.Log;
                    if (result.Stable != null || result.Beta != null)
                    {
                        (updated, log) = OfferUpdate(http, result, version, exe, args);
                    }
                    if (!updated)
                    {
                        Splash.SetStatus(Strings.LauncherStarting);
                        StartApp(Path.Combine(folder, AppExe), args, log);
                    }
                }
                catch (Exception e)
                {
                    // Nothing else sees this thread: every failure is shown after the splash closes.
                    error = e.Message;
                }
                Splash.Close();
            });

        if (error != null)
        {
            MessageBoxW(0, string.Format(CultureInfo.CurrentCulture, Strings.LauncherFailed, error), Strings.WindowTitle, 0x10); // MB_ICONERROR
            return 1;
        }
        if (!updated)
        {
            AppFolder.RemoveOtherVersions(root, version); // the updated launcher does this for its own version
        }
        return 0;
    }

    // Shows the newer releases; on a choice, downloads and verifies it, puts it in place of this launcher and starts
    // it. Returns whether it did, and the log line for the app. A failed update is shown, and the current version starts.
    [SupportedOSPlatform("windows")]
    private static (bool Updated, string Log) OfferUpdate(HttpClient http, UpdateCheckResult result, string version, string exe, string[] args)
    {
        var offered = new[] { result.Stable, result.Beta }.OfType<Release>().ToList();
        var texts = offered
            .Select(release => string.Format(CultureInfo.CurrentCulture, release == result.Stable ? Strings.UpdateToStable : Strings.UpdateToBeta, release.Version))
            .Append(Strings.UpdateSkip)
            .ToList();
        var picked = Splash.Ask(string.Format(CultureInfo.CurrentCulture, Strings.UpdateAvailable, version), texts);
        if (picked >= offered.Count)
        {
            return (false, result.Log + "; skipped");
        }
        var release = offered[picked];
        try
        {
            using var timeout = new CancellationTokenSource(DownloadTimeout);
            var downloaded = UpdateInstaller.DownloadAsync(http, release, exe,
                percent => Splash.SetStatus(string.Format(CultureInfo.CurrentCulture, Strings.UpdateDownloading, release.Version, percent)),
                timeout.Token).GetAwaiter().GetResult();
            UpdateInstaller.Replace(exe, downloaded);
            Splash.SetStatus(Strings.LauncherStarting);
            var start = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Environment.CurrentDirectory };
            foreach (var arg in args)
            {
                start.ArgumentList.Add(arg);
            }
            Process.Start(start)!.Dispose();
            return (true, "");
        }
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException or OperationCanceledException
            or InvalidDataException or System.ComponentModel.Win32Exception)
        {
            MessageBoxW(0, string.Format(CultureInfo.CurrentCulture, Strings.UpdateFailed, e.Message), Strings.WindowTitle, 0x30); // MB_ICONWARNING
            return (false, $"{result.Log}; update to {release.Version} failed: {e.Message}");
        }
    }

    private static Stream OpenPayload() =>
        typeof(Program).Assembly.GetManifestResourceStream("app.zip") ?? throw new InvalidOperationException(Strings.LauncherNoApp);

    // Returns when the app's window is up, or after two minutes (a slow first start under a virus scan); the app keeps
    // running either way.
    private static void StartApp(string exe, string[] args, string log)
    {
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Environment.CurrentDirectory };
        start.Environment[UpdateLogVariable] = log;
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }
        using var app = Process.Start(start)!;
        var waited = Stopwatch.StartNew();
        while (app.MainWindowHandle == 0 && waited.Elapsed < TimeSpan.FromMinutes(2))
        {
            if (app.WaitForExit(50))
            {
                throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, Strings.LauncherAppExited, app.ExitCode));
            }
            app.Refresh();
        }
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(nint owner, string text, string caption, uint type);
}
