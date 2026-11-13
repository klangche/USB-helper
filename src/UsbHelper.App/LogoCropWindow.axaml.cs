using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using UsbHelper.App.ViewModels;

namespace UsbHelper.App;

/// <summary>Crops the logo to 3:1 (#241). The dialog result is true for Save; the crop is then in the view model.</summary>
public partial class LogoCropWindow : Window
{
    // The frame in the stage, as in the .axaml.
    private const double FrameLeft = 40;
    private const double FrameTop = 40;
    private const double FrameWidth = 480;
    private const double FrameHeight = 160;

    private Point? dragFrom;

    public LogoCropWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ShowImage(DataContext as LogoCropViewModel);
        Stage.PointerPressed += (_, e) =>
        {
            dragFrom = e.GetPosition(Stage);
            e.Pointer.Capture(Stage);
        };
        Stage.PointerMoved += (_, e) =>
        {
            if (dragFrom is { } from && DataContext is LogoCropViewModel crop)
            {
                var to = e.GetPosition(Stage);
                crop.Drag((to.X - from.X) / FrameWidth, (to.Y - from.Y) / FrameHeight);
                dragFrom = to;
            }
        };
        Stage.PointerReleased += (_, e) =>
        {
            dragFrom = null;
            e.Pointer.Capture(null);
        };
        Stage.PointerWheelChanged += (_, e) =>
        {
            (DataContext as LogoCropViewModel)?.Wheel(e.Delta.Y);
            e.Handled = true;
        };
    }

    private void ShowImage(LogoCropViewModel? crop)
    {
        Picture.Source = crop == null ? null : new Bitmap(new MemoryStream(crop.Png));
        if (crop != null)
        {
            crop.PropertyChanged += CropChanged;
            Place(crop);
        }
    }

    private void CropChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LogoCropViewModel.Crop))
        {
            Place((LogoCropViewModel)sender!);
        }
    }

    private void Place(LogoCropViewModel crop)
    {
        var (x, y, width, height) = crop.Crop.Place(crop.Aspect, FrameHeight);
        Canvas.SetLeft(Picture, FrameLeft + x);
        Canvas.SetTop(Picture, FrameTop + y);
        Picture.Width = width;
        Picture.Height = height;
    }

    private void FitClick(object? sender, RoutedEventArgs e) => (DataContext as LogoCropViewModel)?.Fit();

    private void SaveClick(object? sender, RoutedEventArgs e) => Close(true);

    private void CancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
