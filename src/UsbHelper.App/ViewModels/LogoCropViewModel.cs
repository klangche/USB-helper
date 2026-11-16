using UsbHelper.App.Settings;

namespace UsbHelper.App.ViewModels;

/// <summary>
/// The crop window (#241): the image (<paramref name="png"/>, <paramref name="aspect"/> times wider than high) in the
/// fixed 3:1 frame, starting at <paramref name="crop"/>. Drag moves it; the slider and the mouse wheel zoom it.
/// </summary>
public sealed class LogoCropViewModel(byte[] png, double aspect, LogoCrop crop) : ObservableObject
{
    /// <summary>The zoom limits, relative to the whole image fitted.</summary>
    public const double MinZoom = 0.25;

    public const double MaxZoom = 8;

    private readonly LogoCrop fit = LogoCrop.Contain(aspect, 1);

    /// <summary>The image shown, at most 2048 pixels long.</summary>
    public byte[] Png { get; } = png;

    /// <summary>Width per unit of height.</summary>
    public double Aspect { get; } = aspect;

    public LogoCrop Crop
    {
        get => crop;
        private set
        {
            if (SetField(ref crop, value))
            {
                OnPropertyChanged(nameof(Zoom));
                OnPropertyChanged(nameof(ZoomLevel));
            }
        }
    }

    /// <summary>1 is the whole image fitted in the frame; kept between <see cref="MinZoom"/> and <see cref="MaxZoom"/>.</summary>
    public double Zoom
    {
        get => Crop.Scale / fit.Scale;
        set => Crop = Crop.Zoom(Math.Clamp(value, MinZoom, MaxZoom) / Zoom);
    }

    /// <summary>The zoom for the slider: 0 is the whole image, each step up doubles it.</summary>
    public double ZoomLevel
    {
        get => Math.Log2(Zoom);
        set => Zoom = Math.Pow(2, value);
    }

    /// <summary>Moves the image by fractions of the frame width and height.</summary>
    public void Drag(double dx, double dy) => Crop = Crop.Move(dx, dy);

    /// <summary>Zooms in (positive) or out by 10 % per wheel step.</summary>
    public void Wheel(double steps) => Zoom *= Math.Pow(1.1, steps);

    /// <summary>Back to the whole image, centred.</summary>
    public void Fit() => Crop = fit;
}
