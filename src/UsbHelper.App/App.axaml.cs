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

    // Startup (#86): the window shows at once; the database, the USB tree and the displays are read after it, off the UI
    // thread, each with its stage in the window. Then the app follows changes. The only place that picks the operating
    // system's detection.
    private static MainViewModel Start(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var log = new EventLog();
        log.Add("USB-helper started");
        // The launcher's update check result (#111); the variable is UsbHelper.Launcher.Program.UpdateLogVariable.
        if (Environment.GetEnvironmentVariable("USBHELPER_UPDATE_CHECK") is { Length: > 0 } update)
        {
            log.Add(update);
        }
        if (!OperatingSystem.IsWindows())
        {
            var unavailable = new MainViewModel(new ComputerIdentity(null, null, MachineName: Environment.MachineName), log: log);
            _ = LoadUnavailable(unavailable, log);
            return unavailable;
        }

        var viewModel = new MainViewModel(WindowsComputerIdentity.Read(), ReadExternalDisplays, log);
        _ = LoadInBackground(viewModel, log, desktop);
        return viewModel;
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
            var displays = await Task.Run(ReadExternalDisplays);
            viewModel.Load(tree, displays);
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
