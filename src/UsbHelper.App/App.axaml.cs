using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using UsbHelper.App.Platform;
using UsbHelper.App.Resources;
using UsbHelper.App.Settings;
using UsbHelper.App.Update;
using UsbHelper.App.ViewModels;
using UsbHelper.Core.Database;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;

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
            if (OperatingSystem.IsMacOS())
            {
                MacNativeMenu.SetAppMenu(this, () => desktop.TryShutdown());
            }
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
        log.SessionStarted(AppVersion.Current);
        var settings = SettingsViewModel.Load(SettingsFile.Default, log);
        var cache = new DatabaseCache(DatabaseCache.NextTo(SettingsFile.Default.Path));
        var platform = PlatformServices.Current();
        if (!platform.IsSupported)
        {
            var unavailable = new MainViewModel(platform.ReadIdentity(), log: log, settings: settings);
            _ = LoadUnavailable(unavailable, log, platform, cache);
            return unavailable;
        }

        // Installed by the .msi (#124) or unzipped (#123): the update takes the matching release asset.
        // The app runs from the app folder below USB-helper.exe (#130).
        var kind = OperatingSystem.IsMacOS() ? UpdateKind.MacOs : UpdateKinds.For(UpdateKinds.TopFolder(AppContext.BaseDirectory), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        log.Add(kind == UpdateKind.Msi ? "Installed by the .msi; updates use the .msi" : "Portable or macOS; updates open the release page");
        var http = new HttpClient();
        var viewModel = new MainViewModel(platform.ReadIdentity(), platform.ReadMonitors, log, settings)
        {
            Update = new UpdateViewModel(http, AppVersion.Current, kind, UpdateFolder,
                msi => InstallMsi(msi, desktop), page => OpenPage(page, desktop, log), log, UpdateCheckTimeout, settings),
        };
        // #341: "Restart" starts the installed version, then this one closes.
        viewModel.RestartRequested = () => Restart(desktop, log);
        var databaseUpdate = new DatabaseUpdate(http, AppVersion.Current, cache, viewModel, log, DatabaseUpdateTimeout, settings, EmbeddedDatabase.Version());
        _ = LoadInBackground(viewModel, log, desktop, platform, cache, databaseUpdate);
        return viewModel;
    }

    // The .msi of an update is kept here, not in %TEMP%, which Windows empties: Windows Installer needs the installed
    // version's .msi to remove it at the next upgrade or an uninstall from Apps (#222).
    private static string UpdateFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "USB-helper-installer");

    // GitHub's answer within this time, or the check is skipped (#111).
    private static readonly TimeSpan UpdateCheckTimeout = TimeSpan.FromSeconds(2);

    // The database check and download of about 30 small files (#290), in the background.
    private static readonly TimeSpan DatabaseUpdateTimeout = TimeSpan.FromSeconds(30);

    // The portable app's update (#131): the release page in the default browser. A browser that does not open is logged.
    private static async void OpenPage(Uri page, IClassicDesktopStyleApplicationLifetime desktop, EventLog log)
    {
        if (desktop.MainWindow is not { } window || !await window.Launcher.LaunchUriAsync(page))
        {
            log.Add($"The browser could not open {page}");
        }
    }

    // Runs the downloaded .msi, which upgrades this install in place and starts the new version when it is done (#124,
    // #147), and closes this app so its files can be replaced.
    private static void InstallMsi(string msi, IClassicDesktopStyleApplicationLifetime desktop)
    {
        Process.Start(MsiInstall.StartInfo(msi))!.Dispose();
        desktop.Shutdown();
    }

    // The app as installed now: its own executable, which a newer install has replaced. When it cannot start, this one
    // stays open and the log says why.
    private static void Restart(IClassicDesktopStyleApplicationLifetime desktop, EventLog log)
    {
        try
        {
            if (Environment.ProcessPath is not { } path)
            {
                log.Add("The installed version could not be started: the app's own path is unknown");
                return;
            }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = false })!.Dispose();
            desktop.Shutdown();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            log.Add($"The installed version could not be started: {e.Message}");
        }
    }

    private static async Task LoadUnavailable(MainViewModel viewModel, EventLog log, PlatformServices platform, DatabaseCache cache)
    {
        await LoadDatabase(viewModel, log, cache);
        viewModel.IsLoading = false;
        viewModel.Notice = Strings.DetectionUnavailable;
        log.Add($"USB detection is not available on {platform.Name} yet"); // After the database result, as on Windows.
    }

    // The database is built into the app (#102), or a newer one was downloaded (#290); every problem of the built-in one
    // ends up in the window's database error list.
    private static async Task LoadDatabase(MainViewModel viewModel, EventLog log, DatabaseCache cache)
    {
        log.Add("Loading database");
        // The log is only written on the UI thread: why a downloaded database is not used is copied over after.
        var notes = new EventLog();
        // The newest format that loads (#334); there is no setting for it (#389).
        var (database, errors, version, format) = await Task.Run(() =>
            DatabaseUpdate.LoadAtStart(cache.Folder, EmbeddedDatabase.Files(), EmbeddedDatabase.Version(), notes));
        foreach (var note in notes.Entries)
        {
            log.Add(note.Text);
        }
        viewModel.DatabaseFormat = format ?? DatabaseFormats.Newest;
        viewModel.OtherFormats = [.. EmbeddedDatabase.Files().Keys.Where(each => each != viewModel.DatabaseFormat).Order()];
        viewModel.LoadDatabase(database, errors, version);
    }

    // Each stage runs off the UI thread, so the window stays responsive and shows what it is reading. Refresh (#206)
    // reads the tree again the same way and replaces the change watcher with one that follows the new tree.
    // The database update (#290) starts once the database in use is loaded, next to the update check and the tree's read.
    private static async Task LoadInBackground(
        MainViewModel viewModel, EventLog log, IClassicDesktopStyleApplicationLifetime desktop, PlatformServices platform, DatabaseCache cache,
        DatabaseUpdate databaseUpdate)
    {
        await LoadDatabase(viewModel, log, cache);
        _ = databaseUpdate.CheckAtStartAsync();
        IDisposable? watch = null;
        var generation = 0;
        desktop.Exit += (_, _) => watch?.Dispose();
        async Task Read()
        {
            watch?.Dispose();
            watch = null;
            // Changes the old watcher already posted belong to the old tree: they are dropped.
            var current = ++generation;
            viewModel.Notice = null;
            watch = await ReadTree(viewModel, log, platform, changes => Dispatcher.UIThread.Post(() =>
            {
                if (current == generation)
                {
                    viewModel.Apply(changes);
                }
            }));
        }
        viewModel.RefreshRequested += () => _ = Read();
        await Read();
    }

    // Reads the USB tree and the displays, shows them, and starts following changes. The watcher, or null when the
    // tree could not be read or changes cannot be followed.
    private static async Task<IDisposable?> ReadTree(MainViewModel viewModel, EventLog log, PlatformServices platform, Action<IReadOnlyList<UsbChange>> onChanges)
    {
        var detector = platform.Detector;
        IReadOnlyList<UsbNode> tree;
        try
        {
            log.Add("Reading USB devices");
            tree = await Task.Run(detector.Enumerate);
            log.Add("Reading displays");
            viewModel.ReadingDisplays();
            var monitors = await Task.Run(platform.ReadMonitors);
            viewModel.Load(tree, monitors);
        }
        catch (Exception e)
        {
            // Nothing else observes this task: every failure must end the loading state and be shown.
            log.Add($"USB detection failed: {e.Message}");
            viewModel.IsLoading = false;
            viewModel.Notice = string.Format(CultureInfo.CurrentCulture, Strings.DetectionFailed, e.Message);
            return null;
        }
        try
        {
            var watch = detector.Watch(tree, onChanges);
            log.Add("Following USB changes");
            return watch;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            // The tree stays visible; it just does not follow changes.
            log.Add($"Live updates are off: {e.Message}");
            viewModel.Notice = string.Format(CultureInfo.CurrentCulture, Strings.LiveUpdatesOff, e.Message);
            return null;
        }
    }
}
