using Avalonia.Controls;
using Avalonia.Interactivity;

namespace UsbHelper.App;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
    }

    private void CloseWindow(object? sender, RoutedEventArgs e) => Close();
}
