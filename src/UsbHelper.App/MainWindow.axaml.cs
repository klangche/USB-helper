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

public partial class MainWindow : Window
{
    private LogWindow? logWindow;

    public MainWindow()
    {
        InitializeComponent();
    }

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

    // The context menu key (or a right-click) on a tree item opens the mark of its line, the same as clicking the line.
    private void OpenCableMark(object? sender, ContextRequestedEventArgs e)
    {
        var item = (e.Source as Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true);
        if (item?.DataContext is NodeViewModel { CanMark: true } node
            && item.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.Name == "CableLine" && button.DataContext == node) is { } line)
        {
            line.Flyout?.ShowAt(line);
            e.Handled = true;
        }
    }

    // Asks where to save, then writes this computer as a computer profile. An event handler: any failure (no
    // access to the folder, a disk error, a name the encoder rejects) is shown instead of ending the app.
    private async void ExportProfile(object? sender, RoutedEventArgs e)
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
