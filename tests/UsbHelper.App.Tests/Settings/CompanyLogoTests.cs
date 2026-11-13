using SkiaSharp;
using UsbHelper.App.Resources;
using UsbHelper.App.Settings;

namespace UsbHelper.App.Tests.Settings;

/// <summary>
/// The company logo (#228): SVG, PNG and WebP are kept as the original and as PNGs 32 to 512 pixels high with alpha;
/// anything else is refused and the previous logo stays. The test images are made here, not committed.
/// </summary>
public sealed class CompanyLogoTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("usbhelper-logo-tests-").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private CompanyLogo Logo => new(Path.Combine(folder, "USB-helper", "branding"));

    // 200 x 100: the left half transparent, the right half opaque red.
    private const string HalfRedSvg = """
        <svg xmlns="http://www.w3.org/2000/svg" {0}>
          <rect x="100" y="0" width="100" height="100" fill="#ff0000" />
        </svg>
        """;

    private string Source(string name, byte[] content)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private string Svg(string name, string sizeAttributes) =>
        Source(name, System.Text.Encoding.UTF8.GetBytes(string.Format(HalfRedSvg, sizeAttributes)));

    private string Raster(string name, SKEncodedImageFormat format) => TestImages.HalfRed(folder, name, format);

    public static TheoryData<string> Sources => ["logo.png", "logo.webp", "logo.svg", "LOGO.PNG", "viewbox-only.svg"];

    [Theory]
    [MemberData(nameof(Sources))]
    public void EverySizeIsWrittenByHeightWithTheAspectRatioAndAlphaKept(string name)
    {
        var source = name switch
        {
            "logo.png" or "LOGO.PNG" => Raster(name, SKEncodedImageFormat.Png),
            "logo.webp" => Raster(name, SKEncodedImageFormat.Webp),
            "logo.svg" => Svg(name, """width="200" height="100" """),
            _ => Svg(name, """viewBox="0 0 200 100" """),
        };
        var logo = Logo;

        logo.Import(source);

        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(logo.Original!));
        Assert.Equal("logo-original" + Path.GetExtension(name).ToLowerInvariant(), Path.GetFileName(logo.Original));
        foreach (var height in CompanyLogo.Heights)
        {
            using var png = SKBitmap.Decode(logo.Png(height));
            Assert.Equal(height, png.Height);
            Assert.Equal(height * 2, png.Width);
            Assert.Equal(0, png.GetPixel(1, height / 2).Alpha); // Transparent stays transparent.
            Assert.Equal(SKColors.Red, png.GetPixel(png.Width - 2, height / 2));
        }
    }

    [Fact]
    public void ThereIsNoLogoUntilOneIsImported()
    {
        Assert.Null(Logo.Original);
    }

    public static TheoryData<string> Refused => ["logo.jpg", "text.png", "no-size.svg", "jpeg-inside.png", "broken.svg"];

    [Theory]
    [MemberData(nameof(Refused))]
    public void AFileThatIsNotASupportedImageIsRefusedAndThePreviousLogoStays(string name)
    {
        var logo = Logo;
        logo.Import(Raster("previous.png", SKEncodedImageFormat.Png));
        var before = Snapshot(logo.Folder);
        var source = name switch
        {
            "logo.jpg" or "jpeg-inside.png" => Raster(name, SKEncodedImageFormat.Jpeg),
            "text.png" => Source(name, "not an image"u8.ToArray()),
            "no-size.svg" => Svg(name, ""),
            _ => Source(name, "<svg"u8.ToArray()),
        };

        var refusal = Assert.Throws<InvalidDataException>(() => logo.Import(source));

        Assert.Equal(name switch
        {
            "logo.jpg" => Strings.LogoWrongType,
            "no-size.svg" => Strings.LogoSvgWithoutSize,
            _ => Strings.LogoUnreadable,
        }, refusal.Message);

        Assert.Equal(before, Snapshot(logo.Folder));
        Assert.Single(Directory.GetDirectories(Path.GetDirectoryName(logo.Folder)!)); // No temporary folder is left.
    }

    [Fact]
    public void ANewLogoReplacesTheWholeSet()
    {
        var logo = Logo;
        logo.Import(Svg("first.svg", """width="200" height="100" """));

        logo.Import(Raster("second.png", SKEncodedImageFormat.Png));

        Assert.Equal(["logo-128.png", "logo-256.png", "logo-32.png", "logo-512.png", "logo-64.png", "logo-original.png"],
            Directory.GetFiles(logo.Folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void RemoveDeletesTheFolder()
    {
        var logo = Logo;
        logo.Import(Raster("logo.png", SKEncodedImageFormat.Png));

        logo.Remove();

        Assert.False(Directory.Exists(logo.Folder));
        Assert.Null(logo.Original);
        logo.Remove(); // Nothing to remove is fine.
    }

    private static Dictionary<string, string> Snapshot(string path) =>
        Directory.GetFiles(path).ToDictionary(f => Path.GetFileName(f), f => Convert.ToBase64String(File.ReadAllBytes(f)));
}
