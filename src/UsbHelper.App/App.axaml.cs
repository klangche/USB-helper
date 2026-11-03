using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using UsbHelper.App.Resources;
using UsbHelper.App.Settings;
using UsbHelper.App.Update;
using UsbHelper.App.ViewModels;
using UsbHelper.Core.Database;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;
using UsbHelper.Detection.Windows;

namespace UsbHelper.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = Start(desktop);
            desktop.MainWindow = new MainWindow { DataContext = viewModel };
            if (viewModel.Update is { } update) // set on Windows only
            {
                // After the window shows, so the check never delays the start (#123).
                desktop.MainWindow.Opened += (_, _) => _ = update.CheckAtStartAsync();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Startup (#86): the window shows at once; the database, the USB tree and the displays are read after it, off the UI
    // thread, each with its stage in the window. Then the app follows changes. The only place that picks the operating
    // system's detection.
    private static MainViewModel Start(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var log = new EventLog();
        log.Add("USB-helper started");
        var settings = SettingsViewModel.Load(SettingsFile.Default, log);
        if (!OperatingSystem.IsWindows())
        {
            var unavailable = new MainViewModel(new ComputerIdentity(null, null, MachineName: Environment.MachineName), log: log, settings: settings);
            _ = LoadUnavailable(unavailable, log);
            return unavailable;
        }

        // Installed by the .msi (#124) or unzipped (#123): the update takes the matching release asset.
        // The app runs from the app folder below USB-helper.exe (#130).
        var kind = UpdateKinds.For(UpdateKinds.TopFolder(AppContext.BaseDirectory), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        log.Add(kind == UpdateKind.Msi ? "Installed by the .msi; updates use the .msi" : "Portable; updates open the release page");
        var viewModel = new MainViewModel(WindowsComputerIdentity.Read(), ReadMonitors, log, settings)
        {
            Update = new UpdateViewModel(new HttpClient(), AppVersion.Current, kind, Path.Combine(Path.GetTempPath(), "USB-helper-update"),
                msi => InstallMsi(msi, desktop), page => OpenPage(page, desktop, log), log, UpdateCheckTimeout, settings),
        };
        _ = LoadInBackground(viewModel, log, desktop);
        return viewModel;
    }

    // GitHub's answer within this time, or the check is skipped (#111).
    private static readonly TimeSpan UpdateCheckTimeout = TimeSpan.FromSeconds(2);

    // The portable app's update (#131): the release page in the default browser. A browser that does not open is logged.
    private static async void OpenPage(Uri page, IClassicDesktopStyleApplicationLifetime desktop, EventLog log)
    {
        if (desktop.MainWindow is not { } window || !await window.Launcher.LaunchUriAsync(page))
        {
            log.Add($"The browser could not open {page}");
        }
    }

    // Runs the downloaded .msi, which upgrades this install in place (#124), and closes this app so its files can be
    // replaced. /passive: a progress bar and no questions. /norestart: if a file is still in use, Windows replaces it at
    // the next restart instead of restarting the computer itself (/passive alone would). The user starts the new version
    // from the Start menu.
    private static void InstallMsi(string msi, IClassicDesktopStyleApplicationLifetime desktop)
    {
        Process.Start(new ProcessStartInfo("msiexec.exe") { UseShellExecute = false, ArgumentList = { "/i", msi, "/passive", "/norestart" } })!.Dispose();
        desktop.Shutdown();
    }

    private static async Task LoadUnavailable(MainViewModel viewModel, EventLog log)
    {
        await LoadDatabase(viewModel, log);
        viewModel.IsLoading = false;
        viewModel.Notice = Strings.DetectionUnavailable;
        log.Add("USB detection is not available on this operating system"); // After the database result, as on Windows.
    }

    // The database is built into the app (#102); every problem ends up in the window's database error list.
    private static async Task LoadDatabase(MainViewModel viewModel, EventLog log)
    {
        log.Add("Loading database");
        var (database, errors) = await Task.Run(() =>
        {
            try
            {
                return (EmbeddedDatabase.Load(), (IReadOnlyList<DatabaseError>)[]);
            }
            catch (DatabaseException e)
            {
                return ((HardwareDatabase?)null, e.Errors);
            }
        });
        viewModel.LoadDatabase(database, errors);
    }

    // Each stage runs off the UI thread, so the window stays responsive and shows what it is reading.
    [SupportedOSPlatform("windows")]
    private static async Task LoadInBackground(MainViewModel viewModel, EventLog log, IClassicDesktopStyleApplicationLifetime desktop)
    {
        await LoadDatabase(viewModel, log);
        var detector = new WindowsUsbDetector();
        IReadOnlyList<UsbNode> tree;
        try
        {
            log.Add("Reading USB devices");
            tree = await Task.Run(detector.Enumerate);
            log.Add("Reading displays");
            viewModel.ReadingDisplays();
            var monitors = await Task.Run(ReadMonitors);
            viewModel.Load(tree, monitors);
        }
        catch (Exception e)
        {
            // Nothing else observes this task: every failure must end the loading state and be shown.
            log.Add($"USB detection failed: {e.Message}");
            viewModel.IsLoading = false;
            viewModel.Notice = string.Format(CultureInfo.CurrentCulture, Strings.DetectionFailed, e.Message);
            return;
        }
        try
        {
            var watch = detector.Watch(tree, changes => Dispatcher.UIThread.Post(() => viewModel.Apply(changes)));
            desktop.Exit += (_, _) => watch.Dispose();
            log.Add("Following USB changes");
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            // The tree stays visible; it just does not follow changes.
            log.Add($"Live updates are off: {e.Message}");
            viewModel.Notice = string.Format(CultureInfo.CurrentCulture, Strings.LiveUpdatesOff, e.Message);
        }
    }

    // Unknown rather than a guess when Windows cannot report the display configuration.
    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<DisplayMonitor>? ReadMonitors()
    {
        try
        {
            return WindowsDisplayReader.ReadMonitors();
        }
        catch (Win32Exception)
        {
            return null;
        }
    }
}
