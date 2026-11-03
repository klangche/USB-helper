using Avalonia.Controls;
using UsbHelper.App.ViewModels;

namespace UsbHelper.App;

public partial class UpdateWindow : Window
{
    public UpdateWindow()
    {
        InitializeComponent();
        // While the .msi downloads, the user cannot close the popup; the app's own shutdown, once msiexec runs, still can.
        // Closing it otherwise is Skip (see MainWindow.ShowUpdate).
        Closing += (_, e) => e.Cancel = !e.IsProgrammatic && e.CloseReason == WindowCloseReason.WindowClosing
            && DataContext is UpdateViewModel { IsOffered: true, IsBusy: true };
    }
}
