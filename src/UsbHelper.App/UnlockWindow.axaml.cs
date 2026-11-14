using Avalonia.Controls;
using Avalonia.Interactivity;
using UsbHelper.App.ViewModels;

namespace UsbHelper.App;

/// <summary>The unlock popup (#238): opened from the top bar, Help > Unlock... and the locked branding in Settings.</summary>
public partial class UnlockWindow : Window
{
    public UnlockWindow()
    {
        InitializeComponent();
    }

    /// <summary>Shows the popup over <paramref name="owner"/> with empty fields; it closes on Cancel or a valid pair.</summary>
    public static void Open(Window owner, UnlockViewModel unlock)
    {
        unlock.Reset();
        var window = new UnlockWindow { DataContext = unlock };
        EventHandler close = (_, _) => window.Close();
        unlock.Unlocked += close;
        window.Closed += (_, _) => unlock.Unlocked -= close;
        _ = window.ShowDialog(owner);
    }

    private void CancelClick(object? sender, RoutedEventArgs e) => Close();
}
