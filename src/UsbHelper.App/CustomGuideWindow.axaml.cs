using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using UsbHelper.App.Resources;
using UsbHelper.App.ViewModels;

namespace UsbHelper.App;

/// <summary>The Custom editor (#300): opened by picking Custom without a valid custom.yaml, or by its Edit... link.</summary>
public partial class CustomGuideWindow : Window
{
    private static readonly FilePickerFileType Yaml = new("YAML") { Patterns = ["*.yaml", "*.yml"] };

    public CustomGuideWindow()
    {
        InitializeComponent();
    }

    // Async void, run by the button: a failure is shown in the window instead of ending the app.
    private async void DownloadBlank(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CustomGuideViewModel editor)
        {
            return;
        }
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Strings.CustomGuideDownloadTitle,
                SuggestedFileName = Core.Database.CustomGuide.FileName,
                DefaultExtension = "yaml",
                FileTypeChoices = [Yaml],
            });
            if (file == null)
            {
                return;
            }
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(editor.Blank);
            editor.Message = string.Format(CultureInfo.CurrentCulture, Strings.CustomGuideDownloaded, file.Name);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            editor.Message = string.Format(CultureInfo.CurrentCulture, Strings.CustomGuideFileFailed, error.Message);
        }
    }

    private async void OpenFile(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CustomGuideViewModel editor)
        {
            return;
        }
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Strings.CustomGuideOpenTitle,
                AllowMultiple = false,
                FileTypeFilter = [Yaml],
            });
            if (files.Count == 0)
            {
                return;
            }
            await using var stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream);
            editor.Text = await reader.ReadToEndAsync();
            editor.Message = null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            editor.Message = string.Format(CultureInfo.CurrentCulture, Strings.CustomGuideFileFailed, error.Message);
        }
    }

    private void CloseClick(object? sender, RoutedEventArgs e) => Close();
}
