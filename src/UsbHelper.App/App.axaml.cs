using System.ComponentModel;
using System.Globalization;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using UsbHelper.App.Resources;
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
            desktop.MainWindow = new MainWindow { DataContext = Start(desktop) };
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Startup: load and validate the database, show the window, enumerate once in the background, then follow
    // changes. The only place that picks the operating system's detection.
    private static MainViewModel Start(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var log = new EventLog();
        log.Add("USB-helper started");
        log.Add("Loading database");
        HardwareDatabase? database = null;
        IReadOnlyList<DatabaseError> errors = [];
        try
        {
            database = HardwareDatabase.Load(Path.Combine(AppContext.BaseDirectory, "database"));
        }
        catch (DatabaseException e)
        {
            errors = e.Errors;
        }

        if (!OperatingSystem.IsWindows())
        {
            var unavailable = new MainViewModel(database, errors, new ComputerIdentity(null, null, MachineName: Environment.MachineName), log: log)
            { Notice = Strings.DetectionUnavailable, IsLoading = false };
            log.Add("USB detection is not available on this operating system"); // After the database result, as on Windows.
            return unavailable;
        }

        var viewModel = new MainViewModel(database, errors, WindowsComputerIdentity.Read(), ReadExternalDisplays, log);
        _ = LoadInBackground(viewModel, log, desktop);
        return viewModel;
    }

    // Reading every hub takes a while, so it runs off the UI thread; the window shows "Reading USB devices" meanwhile.
    [SupportedOSPlatform("windows")]
    private static async Task LoadInBackground(MainViewModel viewModel, EventLog log, IClassicDesktopStyleApplicationLifetime desktop)
    {
        log.Add("Reading USB devices");
        var detector = new WindowsUsbDetector();
        IReadOnlyList<UsbNode> tree;
        try
        {
            tree = await Task.Run(detector.Enumerate);
            viewModel.Load(tree);
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
    private static int? ReadExternalDisplays()
    {
        try
        {
            return WindowsDisplayReader.CountExternalDisplays();
        }
        catch (Win32Exception)
        {
            return null;
        }
    }
}
