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

    // Asks where to save, then writes this computer as a computer profile.
    private async void ExportProfile(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }
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
        try
        {
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(viewModel.ExportProfile());
            ExportResult.Text = string.Format(CultureInfo.CurrentCulture, Strings.ExportSaved, file.Name);
        }
        catch (IOException ex)
        {
            ExportResult.Text = string.Format(CultureInfo.CurrentCulture, Strings.ExportFailed, ex.Message);
        }
    }
}
