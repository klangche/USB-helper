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
    private SettingsViewModel? watched;

    public SettingsWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            Watch(DataContext as SettingsViewModel);
            ShowLogo();
        };
        Closed += (_, _) => Watch(null);
    }

    // Follows the logo of one view model at a time.
    private void Watch(SettingsViewModel? settings)
    {
        if (watched != null)
        {
            watched.PropertyChanged -= LogoChanged;
        }
        watched = settings;
        if (watched != null)
        {
            watched.PropertyChanged += LogoChanged;
        }
    }

    private SettingsViewModel Settings => (SettingsViewModel)DataContext!;

    private void LogoChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.LogoPreview))
        {
            ShowLogo();
        }
    }

    // Read into memory, so the file is not held open and the next import can replace it. A file that cannot be read
    // shows no preview.
    private void ShowLogo()
    {
        LogoImage.Source = null;
        if (watched?.LogoPreview is not { } path)
        {
            return;
        }
        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            LogoImage.Source = new Bitmap(stream);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private async void ChooseLogo(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.SettingsChooseLogo,
            FileTypeFilter = [new FilePickerFileType(Strings.LogoFileType) { Patterns = ["*.svg", "*.png", "*.webp", "*.SVG", "*.PNG", "*.WEBP"] }],
        });
        var path = files.Count == 0 ? null : files[0].TryGetLocalPath();
        // Cancel in the crop window keeps the previous logo (#241).
        if (path != null && Settings.StartCrop(path) is { } crop && await Crop(crop))
        {
            Settings.ImportLogo(path, crop.Crop);
        }
    }

    private async void AdjustCrop(object? sender, RoutedEventArgs e)
    {
        if (Settings.AdjustCrop() is { } crop && await Crop(crop))
        {
            Settings.Recrop(crop.Crop);
        }
    }

    private Task<bool> Crop(LogoCropViewModel crop) => new LogoCropWindow { DataContext = crop }.ShowDialog<bool>(this);

    private void RemoveLogo(object? sender, RoutedEventArgs e) => Settings.RemoveLogo();

    private void CloseWindow(object? sender, RoutedEventArgs e) => Close();
}
