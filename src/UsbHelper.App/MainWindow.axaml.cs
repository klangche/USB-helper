using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using UsbHelper.App.Resources;
using UsbHelper.App.ViewModels;

namespace UsbHelper.App;

public partial class MainWindow : Window, IMainWindowActions
{
    private LogWindow? logWindow;
    private UpdateWindow? updateWindow;

    public MainWindow()
    {
        InitializeComponent();
        // Before the tree item, which would otherwise take Enter to expand or collapse.
        Tree.AddHandler(KeyDownEvent, OpenCableMarkByKey, RoutingStrategies.Tunnel);
        // Only controllers collapse (#142): a double-click, arrow key, Enter or subtree collapse closes no other row.
        Tree.AddHandler(TreeViewItem.CollapsedEvent, KeepOpen);
        // A monitor connected or removed (#133): Windows sends WM_DISPLAYCHANGE, which Avalonia passes on as this event.
        Screens.Changed += (_, _) => (DataContext as MainViewModel)?.DisplaysChanged();
        // The menu and the top bar buttons run the view model's commands, which call back here.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.Window = this;
                ShowBrandingLogo(viewModel);
                viewModel.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(MainViewModel.BrandingLogo))
                    {
                        ShowBrandingLogo(viewModel);
                    }
                };
                if (OperatingSystem.IsMacOS())
                {
                    // macOS shows the menu in the system menu bar, not in the window (#178); Windows keeps the window menu.
                    MenuBar.IsVisible = false;
                    NativeMenu.SetMenu(this, MacNativeMenu.Create(viewModel));
                }
                if (viewModel.Update is { } update)
                {
                    update.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName == nameof(UpdateViewModel.IsOffered))
                        {
                            ShowUpdate(update);
                        }
                    };
                }
            }
        };
    }

    // Read into memory, so the file is not held open and the next logo import can replace it. A file that cannot be
    // read shows no logo.
    private void ShowBrandingLogo(MainViewModel viewModel)
    {
        BrandingLogo.Source = null;
        if (viewModel.BrandingLogo is not { } path)
        {
            return;
        }
        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            BrandingLogo.Source = new Bitmap(stream);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    // The update popup (#131) is open exactly while the update model offers something.
    private void ShowUpdate(UpdateViewModel update)
    {
        if (update.IsOffered && updateWindow == null)
        {
            updateWindow = new UpdateWindow { DataContext = update };
            updateWindow.Closed += (_, _) =>
            {
                updateWindow = null;
                if (update.IsOffered)
                {
                    update.Skip.Execute(null); // closed with the title bar's close button or Alt+F4
                }
            };
            _ = updateWindow.ShowDialog(this);
        }
        else if (!update.IsOffered)
        {
            updateWindow?.Close();
        }
    }

    void IMainWindowActions.ExportProfile() => ExportProfile();

    async void IMainWindowActions.ExportLog()
    {
        if (DataContext is MainViewModel viewModel && await LogWindow.SaveLog(this, viewModel.Log) is { } result)
        {
            ExportResult.Text = result;
        }
    }

    void IMainWindowActions.ShowSettings()
    {
        if (DataContext is MainViewModel viewModel)
        {
            var settings = new SettingsWindow { DataContext = viewModel.Settings };
            settings.Support.DataContext = viewModel.Unlock;
            _ = settings.ShowDialog(this);
        }
    }

    async void IMainWindowActions.ConfirmRefresh()
    {
        if (DataContext is MainViewModel viewModel && await new RefreshWindow().ShowDialog<bool?>(this) is { } clearLog)
        {
            viewModel.RequestRefresh(clearLog);
        }
    }

    void IMainWindowActions.ShowAbout() => _ = new AboutWindow { DataContext = new AboutViewModel() }.ShowDialog(this);

    void IMainWindowActions.Exit() => Close();

    // Takes the screenshot first, so it shows the window as it was when the user chose the menu item. Async void, run by
    // the command: any failure is shown instead of ending the app.
    async void IMainWindowActions.ExportSetup()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }
        try
        {
            var screenshot = Screenshot();
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Strings.ExportSetupTitle,
                SuggestedFileName = viewModel.SetupFileName(DateTime.Now),
                DefaultExtension = "zip",
                FileTypeChoices = [new FilePickerFileType("Zip") { Patterns = ["*.zip"] }],
            });
            if (file == null)
            {
                return;
            }
            await using var stream = await file.OpenWriteAsync();
            viewModel.ExportSetup(stream, screenshot, DateOnly.FromDateTime(DateTime.Now));
            ExportResult.Text = string.Format(CultureInfo.CurrentCulture, Strings.ExportSetupSaved, file.Name);
        }
        catch (Exception ex)
        {
            ExportResult.Text = string.Format(CultureInfo.CurrentCulture, Strings.ExportSetupFailed, ex.Message);
        }
    }

    // The window's content as drawn, at the screen's scale, as PNG.
    private byte[] Screenshot()
    {
        var scale = RenderScaling;
        var size = new PixelSize((int)Math.Ceiling(Bounds.Width * scale), (int)Math.Ceiling(Bounds.Height * scale));
        using var bitmap = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
        bitmap.Render(this);
        using var png = new MemoryStream();
        bitmap.Save(png, new PngBitmapEncoderOptions());
        return png.ToArray();
    }

    // Fire and forget: a clipboard Windows cannot open leaves the old content, nothing else breaks.
    void IMainWindowActions.CopyText(string text) => _ = Clipboard?.SetTextAsync(text);

    // One log window at a time: a second click brings the open one to the front.
    private void OpenLog(object? sender, RoutedEventArgs e)
    {
        if (logWindow != null)
        {
            logWindow.Activate();
            return;
        }
        if (DataContext is MainViewModel viewModel)
        {
            logWindow = new LogWindow(viewModel.Log);
            logWindow.Closed += (_, _) => logWindow = null;
            logWindow.Show(this);
        }
    }

    private static void KeepOpen(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TreeViewItem { DataContext: NodeViewModel node } item && !node.CanExpand)
        {
            item.SetCurrentValue(TreeViewItem.IsExpandedProperty, true);
        }
    }

    // The context menu key (or a right-click) on a tree item opens the mark of its line, the same as clicking its "+".
    private void OpenCableMark(object? sender, ContextRequestedEventArgs e) => e.Handled = ShowCableMark(e.Source);

    // Enter on the selected tree item does the same when its line can be marked: the "+" buttons are not in the Tab
    // order of the tree. Items without a markable line keep Enter for expanding and collapsing.
    private void OpenCableMarkByKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = ShowCableMark(e.Source);
        }
    }

    private static bool ShowCableMark(object? source)
    {
        var item = (source as Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true);
        if (item?.DataContext is NodeViewModel { CanMark: true } node
            && item.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.Name == "CableMark" && button.DataContext == node) is { } plus)
        {
            plus.Flyout?.ShowAt(plus);
            return true;
        }
        return false;
    }

    // Asks where to save, then writes this computer as a computer profile. Async void, run by the command: any failure
    // (no access to the folder, a disk error, a name the encoder rejects) is shown instead of ending the app.
    private async void ExportProfile()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Strings.ExportTitle,
                SuggestedFileName = viewModel.ExportFileName,
                DefaultExtension = "yaml",
                FileTypeChoices = [new FilePickerFileType("YAML") { Patterns = ["*.yaml"] }],
            });
            if (file == null)
            {
                return;
            }
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(viewModel.ExportProfile());
            ExportResult.Text = string.Format(CultureInfo.CurrentCulture, Strings.ExportSaved, file.Name);
        }
        catch (Exception ex)
        {
            ExportResult.Text = string.Format(CultureInfo.CurrentCulture, Strings.ExportFailed, ex.Message);
        }
    }
}
