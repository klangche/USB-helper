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
        log.Add("USB-helper started");
        var settings = SettingsViewModel.Load(SettingsFile.Default, log);
        var platform = PlatformServices.Current();
        if (!platform.IsSupported)
        {
            var unavailable = new MainViewModel(platform.ReadIdentity(), log: log, settings: settings);
            _ = LoadUnavailable(unavailable, log, platform);
            return unavailable;
        }

        // Installed by the .msi (#124) or unzipped (#123): the update takes the matching release asset.
        // The app runs from the app folder below USB-helper.exe (#130).
        var kind = OperatingSystem.IsMacOS() ? UpdateKind.MacOs : UpdateKinds.For(UpdateKinds.TopFolder(AppContext.BaseDirectory), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        log.Add(kind == UpdateKind.Msi ? "Installed by the .msi; updates use the .msi" : "Portable or macOS; updates open the release page");
        var viewModel = new MainViewModel(platform.ReadIdentity(), platform.ReadMonitors, log, settings)
        {
            Update = new UpdateViewModel(new HttpClient(), AppVersion.Current, kind, Path.Combine(Path.GetTempPath(), "USB-helper-update"),
                msi => InstallMsi(msi, desktop), page => OpenPage(page, desktop, log), log, UpdateCheckTimeout, settings),
        };
        _ = LoadInBackground(viewModel, log, desktop, platform);
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

    // Runs the downloaded .msi, which upgrades this install in place and starts the new version when it is done (#124,
    // #147), and closes this app so its files can be replaced.
    private static void InstallMsi(string msi, IClassicDesktopStyleApplicationLifetime desktop)
    {
        Process.Start(MsiInstall.StartInfo(msi))!.Dispose();
        desktop.Shutdown();
    }

    private static async Task LoadUnavailable(MainViewModel viewModel, EventLog log, PlatformServices platform)
    {
        await LoadDatabase(viewModel, log);
        viewModel.IsLoading = false;
        viewModel.Notice = Strings.DetectionUnavailable;
        log.Add($"USB detection is not available on {platform.Name} yet"); // After the database result, as on Windows.
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

    // Each stage runs off the UI thread, so the window stays responsive and shows what it is reading. Refresh (#206)
    // reads the tree again the same way and replaces the change watcher with one that follows the new tree.
    private static async Task LoadInBackground(MainViewModel viewModel, EventLog log, IClassicDesktopStyleApplicationLifetime desktop, PlatformServices platform)
    {
        await LoadDatabase(viewModel, log);
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
        viewModel.RefreshRequested += cleared => { _ = Read(); };
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
