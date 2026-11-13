using UsbHelper.App.Settings;

namespace UsbHelper.App.Tests.Settings;

/// <summary>The crop math of the logo (#241): a fixed 3:1 frame, the image placed in it by centre and scale.</summary>
public sealed class LogoCropTests
{
    [Theory]
    [InlineData(100, 400, 137.5, 0, 25, 100)] // Tall: as high as the frame, centred.
    [InlineData(100, 100, 100, 0, 100, 100)] // Square: as high as the frame, centred.
    [InlineData(900, 100, 0, 33.333, 300, 33.333)] // Wide: as wide as the frame, centred.
    [InlineData(300, 100, 0, 0, 300, 100)] // Exactly 3:1: fills the frame.
    public void ContainFitsTheWholeImageCentredInTheFrame(int width, int height, double x, double y, double placedWidth, double placedHeight)
    {
        var crop = LogoCrop.Contain(width, height);

        var place = crop.Place((double)width / height, frameHeight: 100);

        Assert.Equal(x, place.X, 3);
        Assert.Equal(y, place.Y, 3);
        Assert.Equal(placedWidth, place.Width, 3);
        Assert.Equal(placedHeight, place.Height, 3);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(32)]
    [InlineData(512)]
    public void TheFrameIsAlwaysThreeTimesWiderThanHigh(int height)
    {
        Assert.Equal(3, LogoCrop.Ratio);
        Assert.Equal(height * 3, LogoCrop.FrameWidth(height));
    }

    [Fact]
    public void MovingShiftsTheImageByAFractionOfTheFrame()
    {
        var crop = LogoCrop.Contain(100, 100).Move(0.1, -0.2);

        var place = crop.Place(1, frameHeight: 100);

        Assert.Equal(100 + 30, place.X, 3);
        Assert.Equal(-20, place.Y, 3);
    }

    [Fact]
    public void TheImageCentreStaysInsideTheFrame()
    {
        var crop = LogoCrop.Contain(100, 100).Move(5, -5);

        Assert.Equal(1, crop.CenterX);
        Assert.Equal(0, crop.CenterY);
    }

    [Fact]
    public void ZoomingKeepsThePointInTheFrameCentreInPlace()
    {
        var crop = LogoCrop.Contain(100, 100).Move(0.1, 0).Zoom(2);

        var place = crop.Place(1, frameHeight: 100);

        Assert.Equal(2, crop.Scale, 3);
        Assert.Equal(200, place.Width, 3);
        Assert.Equal(200, place.Height, 3);
        // The image centre was 30 to the right of the frame centre; now it is 60 to the right.
        Assert.Equal(150 + 60 - 100, place.X, 3);
        Assert.Equal(50 - 100, place.Y, 3);
    }
}
