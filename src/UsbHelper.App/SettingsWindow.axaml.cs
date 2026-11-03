using Avalonia.Controls;
using Avalonia.Interactivity;

namespace UsbHelper.App;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void CloseWindow(object? sender, RoutedEventArgs e) => Close();
}
