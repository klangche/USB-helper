using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using UsbHelper.App.Resources;
using UsbHelper.App.ViewModels;

namespace UsbHelper.App;

public partial class MainWindow : Window, IMainWindowActions
{
    private LogWindow? logWindow;

    public MainWindow()
    {
        InitializeComponent();
        // Before the tree item, which would otherwise take Enter to expand or collapse.
        Tree.AddHandler(KeyDownEvent, OpenCableMarkByKey, RoutingStrategies.Tunnel);
        // Only the host collapses (#132): a double-click, arrow key, Enter or subtree collapse closes no row below it.
        Tree.AddHandler(TreeViewItem.CollapsedEvent, KeepOpen);
        // A monitor connected or removed (#133): Windows sends WM_DISPLAYCHANGE, which Avalonia passes on as this event.
        Screens.Changed += (_, _) => (DataContext as MainViewModel)?.DisplaysChanged();
        // The menu and the top bar buttons run the view model's commands, which call back here.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.Window = this;
            }
        };
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
            _ = new SettingsWindow { DataContext = viewModel.Settings }.ShowDialog(this);
        }
    }

    void IMainWindowActions.ShowAbout() => _ = new AboutWindow { DataContext = new AboutViewModel() }.ShowDialog(this);

    void IMainWindowActions.Exit() => Close();

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
        if (e.Source is TreeViewItem { DataContext: NodeViewModel node } item && node.Id != NodeViewModel.HostId)
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
