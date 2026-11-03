using Avalonia.Controls;
using UsbHelper.App.ViewModels;

namespace UsbHelper.App;

public partial class UpdateWindow : Window
{
    public UpdateWindow()
    {
        InitializeComponent();
        // While the .msi downloads, the popup stays open. Closing it otherwise is Skip (see MainWindow.ShowUpdate).
        Closing += (_, e) => e.Cancel = DataContext is UpdateViewModel { IsOffered: true, IsBusy: true };
    }
}
