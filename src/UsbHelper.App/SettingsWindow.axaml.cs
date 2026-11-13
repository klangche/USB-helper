using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using UsbHelper.App.Resources;
using UsbHelper.App.ViewModels;

namespace UsbHelper.App;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is SettingsViewModel settings)
            {
                settings.PropertyChanged += LogoChanged;
                ShowLogo();
            }
        };
        Closed += (_, _) =>
        {
            if (DataContext is SettingsViewModel settings)
            {
                settings.PropertyChanged -= LogoChanged;
            }
        };
    }

    private SettingsViewModel Settings => (SettingsViewModel)DataContext!;

    private void LogoChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.LogoPreview))
        {
            ShowLogo();
        }
    }

    // Read into memory, so the file is not held open and the next import can replace it.
    private void ShowLogo()
    {
        var path = Settings.LogoPreview;
        if (path == null)
        {
            LogoImage.Source = null;
            return;
        }
        using var stream = new MemoryStream(File.ReadAllBytes(path));
        LogoImage.Source = new Bitmap(stream);
    }

    private async void ChooseLogo(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.SettingsChooseLogo,
            FileTypeFilter = [new FilePickerFileType(Strings.LogoFileType) { Patterns = ["*.svg", "*.png", "*.webp", "*.SVG", "*.PNG", "*.WEBP"] }],
        });
        var path = files.Count == 0 ? null : files[0].TryGetLocalPath();
        if (path != null)
        {
            Settings.ImportLogo(path);
        }
    }

    private void RemoveLogo(object? sender, RoutedEventArgs e) => Settings.RemoveLogo();

    private void CloseWindow(object? sender, RoutedEventArgs e) => Close();
}
