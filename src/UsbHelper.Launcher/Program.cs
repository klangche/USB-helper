using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using UsbHelper.Launcher.Resources;

namespace UsbHelper.Launcher;

/// <summary>
/// USB-helper.exe (#110): shows the splash at once, unpacks the app it carries on the first start of a version, starts
/// it, closes the splash when the app's window is up, then removes older versions.
/// </summary>
internal static partial class Program
{
    private const string AppExe = "UsbHelper.App.exe";

    public static int Main(string[] args) => OperatingSystem.IsWindows() ? Run(args) : 1;

    [SupportedOSPlatform("windows")]
    private static int Run(string[] args)
    {
        // The version given to the build (-p:Version), without the commit the SDK appends after "+".
        var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion.Split('+')[0];
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "USB-helper");
        string? error = null;

        Splash.Run(
            string.Format(CultureInfo.CurrentCulture, Strings.LauncherVersion, version),
            AppFolder.IsComplete(Path.Combine(root, version)) ? Strings.LauncherStarting : Strings.LauncherUnpacking,
            () =>
            {
                try
                {
                    var folder = AppFolder.Prepare(root, version, OpenPayload);
                    Splash.SetStatus(Strings.LauncherStarting);
                    StartApp(Path.Combine(folder, AppExe), args);
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
        AppFolder.RemoveOtherVersions(root, version);
        return 0;
    }

    private static Stream OpenPayload() =>
        typeof(Program).Assembly.GetManifestResourceStream("app.zip") ?? throw new InvalidOperationException(Strings.LauncherNoApp);

    // Returns when the app's window is up, or after two minutes (a slow first start under a virus scan); the app keeps
    // running either way.
    private static void StartApp(string exe, string[] args)
    {
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Environment.CurrentDirectory };
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
