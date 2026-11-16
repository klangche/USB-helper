namespace UsbHelper.App.Settings;

/// <summary>
/// Where the logo image sits in its frame (#241). The frame is always <see cref="Ratio"/> times wider than high. The
/// image centre is at <paramref name="CenterX"/>, <paramref name="CenterY"/> as fractions of the frame width and height
/// (0.5, 0.5 is the middle), and the image is <paramref name="Scale"/> frame heights high. Frame outside the image is
/// transparent; image outside the frame is cut off.
/// </summary>
public sealed record LogoCrop(double CenterX, double CenterY, double Scale)
{
    /// <summary>Width : height of every logo.</summary>
    public const int Ratio = 3;

    /// <summary>The width of a frame <paramref name="height"/> high.</summary>
    public static int FrameWidth(int height) => height * Ratio;

    /// <summary>The whole image as large as fits, centred.</summary>
    public static LogoCrop Contain(double imageWidth, double imageHeight) =>
        new(0.5, 0.5, Math.Min(1, Ratio / (imageWidth / imageHeight)));

    /// <summary>Moved by fractions of the frame width and height; the image centre stays inside the frame.</summary>
    public LogoCrop Move(double dx, double dy) =>
        this with { CenterX = Math.Clamp(CenterX + dx, 0, 1), CenterY = Math.Clamp(CenterY + dy, 0, 1) };

    /// <summary><paramref name="factor"/> times larger, around the frame centre.</summary>
    public LogoCrop Zoom(double factor) =>
        new LogoCrop(0.5, 0.5, Scale * factor).Move((CenterX - 0.5) * factor, (CenterY - 0.5) * factor);

    /// <summary>The image rectangle in a frame <paramref name="frameHeight"/> high, for an image this wide per unit of height.</summary>
    public (double X, double Y, double Width, double Height) Place(double imageAspect, double frameHeight)
    {
        var height = Scale * frameHeight;
        var width = height * imageAspect;
        return (CenterX * frameHeight * Ratio - width / 2, CenterY * frameHeight - height / 2, width, height);
    }
}
