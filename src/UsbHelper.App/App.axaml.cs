using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using UsbHelper.App.Resources;
using UsbHelper.App.ViewModels;
using UsbHelper.Core.Database;
using UsbHelper.Core.Detection;
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

    // Startup: load and validate the database, enumerate once, then follow changes.
    // The only place that picks the operating system's detection.
    private static MainViewModel Start(IClassicDesktopStyleApplicationLifetime desktop)
    {
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
            return new MainViewModel(database, errors, new ComputerIdentity(null, null)) { Notice = Strings.DetectionUnavailable };
        }

        var viewModel = new MainViewModel(database, errors, WindowsComputerIdentity.Read());
        var detector = new WindowsUsbDetector();
        IReadOnlyList<UsbNode> tree;
        try
        {
            tree = detector.Enumerate();
        }
        catch (Win32Exception e)
        {
            viewModel.Notice = string.Format(CultureInfo.CurrentCulture, Strings.DetectionFailed, e.Message);
            return viewModel;
        }
        viewModel.Load(tree);
        try
        {
            var watch = detector.Watch(tree, changes => Dispatcher.UIThread.Post(() => viewModel.Apply(changes)));
            desktop.Exit += (_, _) => watch.Dispose();
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            // The tree stays visible; it just does not follow changes.
            viewModel.Notice = string.Format(CultureInfo.CurrentCulture, Strings.LiveUpdatesOff, e.Message);
        }
        return viewModel;
    }
}
