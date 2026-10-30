using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using UsbHelper.App.Resources;
using UsbHelper.App.ViewModels;

namespace UsbHelper.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
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
