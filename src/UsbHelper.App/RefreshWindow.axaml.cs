using Avalonia.Controls;
using Avalonia.Interactivity;

namespace UsbHelper.App;

/// <summary>Asks how to refresh (#206). The dialog result is false for refresh only, true to clear the log too, null to cancel.</summary>
public partial class RefreshWindow : Window
{
    public RefreshWindow()
    {
        InitializeComponent();
    }

    private void RefreshOnlyClick(object? sender, RoutedEventArgs e) => Close(false);

    private void RefreshAndClearClick(object? sender, RoutedEventArgs e) => Close(true);

    private void CancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
