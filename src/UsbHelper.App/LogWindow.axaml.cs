using System.Collections.Specialized;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using UsbHelper.App.Resources;
using UsbHelper.App.ViewModels;

namespace UsbHelper.App;

public partial class LogWindow : Window
{
    public LogWindow()
    {
        InitializeComponent();
    }

    public LogWindow(LogViewModel viewModel) : this()
    {
        DataContext = viewModel;
        Opened += (_, _) => Entries.ScrollToEnd();
        viewModel.Lines.CollectionChanged += ScrollToNewest;
        Closed += (_, _) => viewModel.Lines.CollectionChanged -= ScrollToNewest;
    }

    // After layout, so the new line is measured before scrolling to it.
    private void ScrollToNewest(object? sender, NotifyCollectionChangedEventArgs e) =>
        Dispatcher.UIThread.Post(Entries.ScrollToEnd, DispatcherPriority.Background);

    // Asks where to save, then writes the log as plain text. Same style as the profile export in MainWindow: any
    // failure is shown instead of ending the app.
    private async void ExportLog(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LogViewModel viewModel)
        {
            return;
        }
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Strings.LogExportTitle,
                SuggestedFileName = viewModel.ExportFileName,
                DefaultExtension = "txt",
                FileTypeChoices = [new FilePickerFileType(Strings.LogFileType) { Patterns = ["*.txt"] }],
            });
            if (file == null)
            {
                return;
            }
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(viewModel.Export());
            ExportResult.Text = string.Format(CultureInfo.CurrentCulture, Strings.LogExportSaved, file.Name);
        }
        catch (Exception ex)
        {
            ExportResult.Text = string.Format(CultureInfo.CurrentCulture, Strings.LogExportFailed, ex.Message);
        }
    }
}
