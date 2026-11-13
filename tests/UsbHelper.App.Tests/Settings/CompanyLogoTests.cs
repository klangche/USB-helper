using SkiaSharp;
using UsbHelper.App.Resources;
using UsbHelper.App.Settings;

namespace UsbHelper.App.Tests.Settings;

/// <summary>
/// The company logo (#228, #241): SVG, PNG and WebP are kept as the original, the crop, and 3:1 PNGs 32 to 512 pixels
/// high with alpha; an SVG also as a 3:1 SVG. Anything else is refused and the previous logo stays. The test images are made here, not committed.
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

    private string AnySource(string name) => name switch
    {
        "logo.png" or "LOGO.PNG" => Raster(name, SKEncodedImageFormat.Png),
        "logo.webp" => Raster(name, SKEncodedImageFormat.Webp),
        "logo.svg" => Svg(name, """width="200" height="100" """),
        _ => Svg(name, """viewBox="0 0 200 100" """),
    };

    [Theory]
    [MemberData(nameof(Sources))]
    public void EverySizeIsThreeToOneWithTheWholeImageCentredAndAlphaKept(string name)
    {
        var source = AnySource(name);
        var logo = Logo;

        logo.Import(source);

        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(logo.Original!));
        Assert.Equal("logo-original" + Path.GetExtension(name).ToLowerInvariant(), Path.GetFileName(logo.Original));
        Assert.Equal(LogoCrop.Contain(200, 100), logo.Crop);
        foreach (var height in CompanyLogo.Heights)
        {
            // The 2:1 image is as high as the frame: frame | transparent half | red half | frame.
            using var png = SKBitmap.Decode(logo.Png(height));
            Assert.Equal(height, png.Height);
            Assert.Equal(height * 3, png.Width);
            Assert.Equal(0, png.GetPixel(1, height / 2).Alpha); // Frame outside the image is transparent.
            Assert.Equal(0, png.GetPixel(height, height / 2).Alpha); // Transparent stays transparent.
            Assert.Equal(SKColors.Red, png.GetPixel(height * 2, height / 2));
            Assert.Equal(0, png.GetPixel(height * 3 - 2, height / 2).Alpha);
        }
    }

    [Theory]
    [MemberData(nameof(Sources))]
    public void TheCropIsAppliedAndKept(string name)
    {
        var logo = Logo;
        // Twice as high as the frame and centred: only the middle of the image shows, the red half on the right.
        var crop = new LogoCrop(0.5, 0.5, 2);

        logo.Import(AnySource(name), crop);

        Assert.Equal(crop, logo.Crop);
        Assert.Equal(crop, new CompanyLogo(logo.Folder).Crop); // Read back from crop.json.
        using var png = SKBitmap.Decode(logo.Png(32));
        Assert.Equal(96, png.Width);
        Assert.Equal(0, png.GetPixel(1, 16).Alpha);
        Assert.Equal(SKColors.Red, png.GetPixel(94, 16));
    }

    [Theory]
    [MemberData(nameof(Sources))]
    public void RecropRedrawsFromTheOriginal(string name)
    {
        var logo = Logo;
        logo.Import(AnySource(name));
        var original = File.ReadAllBytes(logo.Original!);
        var crop = LogoCrop.Contain(200, 100).Move(-0.5, 0); // The image centre on the left edge: the red half shows.

        logo.Recrop(crop);

        Assert.Equal(original, File.ReadAllBytes(logo.Original!));
        Assert.Equal(crop, logo.Crop);
        using var png = SKBitmap.Decode(logo.Png(64));
        Assert.Equal(SKColors.Red, png.GetPixel(32, 32));
        Assert.Equal(0, png.GetPixel(100, 32).Alpha);
    }

    [Fact]
    public void AnSvgIsAlsoKeptAsAVectorThreeToOneWrapper()
    {
        var logo = Logo;

        logo.Import(Svg("logo.svg", """width="200" height="100" """));

        var svg = System.Xml.Linq.XDocument.Load(logo.Svg!);
        Assert.Equal("svg", svg.Root!.Name.LocalName);
        Assert.Equal("0 0 3 1", svg.Root.Attribute("viewBox")!.Value);
        Assert.Single(svg.Descendants(), e => e.Name.LocalName == "rect"); // The original drawing, not a bitmap.
        Assert.DoesNotContain(svg.Descendants(), e => e.Name.LocalName == "image");
    }

    [Fact]
    public void ARasterLogoHasNoSvg()
    {
        var logo = Logo;
        logo.Import(Svg("first.svg", """width="200" height="100" """));

        logo.Import(Raster("second.png", SKEncodedImageFormat.Png));

        Assert.Null(logo.Svg);
    }

    [Theory]
    [InlineData("logo.png")]
    [InlineData("logo.svg")]
    public void ALogoStoredBeforeTheCropToolGetsTheDefaultCrop(string name)
    {
        var logo = Logo;
        logo.Import(AnySource(name));
        File.Delete(Path.Combine(logo.Folder, "crop.json"));
        File.Delete(Path.Combine(logo.Folder, "logo.svg")); // Missing if the file is there at all.
        File.WriteAllBytes(logo.Png(32), File.ReadAllBytes(logo.Png(512))); // Any old content.

        var upgraded = new CompanyLogo(logo.Folder);

        Assert.NotNull(upgraded.Original);
        Assert.Equal(LogoCrop.Contain(200, 100), upgraded.Crop);
        using var png = SKBitmap.Decode(upgraded.Png(32));
        Assert.Equal(96, png.Width);
        Assert.Equal(name.EndsWith(".svg", StringComparison.Ordinal), upgraded.Svg != null);
    }

    [Fact]
    public void ThePreviewKeepsTheAspectRatioAndIsAtMost2048PixelsLong()
    {
        var (png, aspect) = CompanyLogo.Preview(Svg("wide.svg", """viewBox="0 0 9000 1000" """));

        using var bitmap = SKBitmap.Decode(png);
        Assert.Equal(9, aspect);
        Assert.Equal(2048, bitmap.Width);
        Assert.Equal(228, bitmap.Height);
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

    [Theory]
    [InlineData("wide.png", 2000, 100)]
    [InlineData("tall.png", 100, 2000)]
    [InlineData("wide.svg", 20000, 1)]
    [InlineData("tall.svg", 1, 20)]
    public void AnyAspectRatioIsCroppedToThreeToOne(string name, int width, int height)
    {
        var source = name.EndsWith(".svg", StringComparison.Ordinal)
            ? Svg(name, $"""viewBox="0 0 {width} {height}" """)
            : TestImages.Blank(folder, name, width, height);
        var logo = Logo;

        logo.Import(source);

        Assert.Equal(LogoCrop.Contain(width, height), logo.Crop);
        using var png = SKBitmap.Decode(logo.Png(32));
        Assert.Equal(96, png.Width);
    }

    [Fact]
    public void AnImageOverFiftyMegapixelsIsRefusedBeforeItIsDecoded()
    {
        var logo = Logo;
        logo.Import(Raster("previous.png", SKEncodedImageFormat.Png));
        var before = Snapshot(logo.Folder);
        var source = Source("huge.png", TestImages.PngHeader(10_000, 5_001));

        Assert.Equal(Strings.LogoTooLarge, Assert.Throws<InvalidDataException>(() => logo.Import(source)).Message);
        Assert.Equal(Strings.LogoTooLarge, Assert.Throws<InvalidDataException>(() => CompanyLogo.Preview(source)).Message);
        Assert.Equal(before, Snapshot(logo.Folder));
    }

    [Fact]
    public void AFailedSwapPutsThePreviousLogoBack()
    {
        var logo = Logo;
        logo.Import(Raster("previous.png", SKEncodedImageFormat.Png));
        var before = Snapshot(logo.Folder);
        var failing = new CompanyLogo(logo.Folder)
        {
            // The new set fails to move into place after the old one was moved aside.
            MoveFolder = (from, to) =>
            {
                if (from.EndsWith(".new", StringComparison.Ordinal))
                {
                    throw new IOException("disk full");
                }
                Directory.Move(from, to);
            },
        };

        Assert.Throws<IOException>(() => failing.Import(Svg("next.svg", """width="200" height="100" """)));

        Assert.Equal(before, Snapshot(logo.Folder));
        Assert.Single(Directory.GetDirectories(Path.GetDirectoryName(logo.Folder)!)); // No .old or .new is left.
    }

    [Fact]
    public void ALogoLeftAsideByAnInterruptedSwapIsRestored()
    {
        var logo = Logo;
        logo.Import(Raster("previous.png", SKEncodedImageFormat.Png));
        var before = Snapshot(logo.Folder);
        Directory.Move(logo.Folder, logo.Folder + ".old"); // The app stopped between the two moves.

        Assert.NotNull(new CompanyLogo(logo.Folder).Original);
        Assert.Equal(before, Snapshot(logo.Folder));
    }

    [Fact]
    public void RemoveAlsoDeletesALogoLeftAside()
    {
        var logo = Logo;
        logo.Import(Raster("previous.png", SKEncodedImageFormat.Png));
        Directory.Move(logo.Folder, logo.Folder + ".old");

        logo.Remove();

        Assert.Null(logo.Original);
        Assert.False(Directory.Exists(logo.Folder + ".old"));
    }

    [Fact]
    public void ANewLogoReplacesTheWholeSet()
    {
        var logo = Logo;
        logo.Import(Svg("first.svg", """width="200" height="100" """));

        logo.Import(Raster("second.png", SKEncodedImageFormat.Png));

        Assert.Equal(["crop.json", "logo-128.png", "logo-256.png", "logo-32.png", "logo-512.png", "logo-64.png", "logo-original.png"],
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
