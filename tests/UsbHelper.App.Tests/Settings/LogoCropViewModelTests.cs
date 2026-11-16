using UsbHelper.App.Settings;
using UsbHelper.App.ViewModels;

namespace UsbHelper.App.Tests.Settings;

/// <summary>The crop window (#241): drag to move, zoom with the slider or the wheel, Fit for the whole image.</summary>
public sealed class LogoCropViewModelTests
{
    // A square image: the whole of it fits at one frame height.
    private static LogoCropViewModel Square() => new([], 1, LogoCrop.Contain(1, 1));

    [Fact]
    public void ZoomIsRelativeToTheWholeImageAndLimited()
    {
        var crop = Square();
        Assert.Equal(1, crop.Zoom);

        crop.Zoom = 2;
        Assert.Equal(2, crop.Crop.Scale, 6);

        crop.Zoom = 1000;
        Assert.Equal(LogoCropViewModel.MaxZoom, crop.Zoom, 6);
        crop.Zoom = 0;
        Assert.Equal(LogoCropViewModel.MinZoom, crop.Zoom, 6);
    }

    [Fact]
    public void TheSliderZoomsInDoublings()
    {
        var crop = Square();
        Assert.Equal(0, crop.ZoomLevel);

        crop.ZoomLevel = 1;
        Assert.Equal(2, crop.Zoom, 6);
        crop.ZoomLevel = -2;
        Assert.Equal(0.25, crop.Zoom, 6);
        Assert.Equal(Math.Log2(LogoCropViewModel.MinZoom), crop.ZoomLevel, 6);
    }

    [Fact]
    public void TheWheelZoomsInStepsAndDragMoves()
    {
        var crop = Square();
        var changed = new List<string?>();
        crop.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        crop.Wheel(1);
        Assert.Equal(1.1, crop.Zoom, 6);
        crop.Wheel(-1);
        Assert.Equal(1, crop.Zoom, 6);

        crop.Drag(0.1, 0.2);
        Assert.Equal(new LogoCrop(0.6, 0.7, 1), crop.Crop with { CenterX = Math.Round(crop.Crop.CenterX, 6), CenterY = Math.Round(crop.Crop.CenterY, 6) });
        Assert.Contains(nameof(LogoCropViewModel.Crop), changed);
        Assert.Contains(nameof(LogoCropViewModel.Zoom), changed);
        Assert.Contains(nameof(LogoCropViewModel.ZoomLevel), changed);

        crop.Fit();
        Assert.Equal(LogoCrop.Contain(1, 1), crop.Crop);
    }
}
