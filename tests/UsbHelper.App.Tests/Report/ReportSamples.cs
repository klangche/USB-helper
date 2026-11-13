using SkiaSharp;
using Svg.Skia;
using UsbHelper.App.Report;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;

namespace UsbHelper.App.Tests.Report;

/// <summary>Invented report content for the tests and the sample PDFs: no real company, person or serial number.</summary>
internal static class ReportSamples
{
    public const string Company = "Halden Systems AB";
    public const string Room = "Board room 4.12";
    public const string Device = "Logitech Rally Bar";

    private const string LogoSvg = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 120 120" width="120" height="120">
          <rect x="6" y="6" width="108" height="108" rx="26" fill="#1F3A5F"/>
          <path d="M36 34 V86 M84 34 V86 M36 60 H84" stroke="#FFFFFF" stroke-width="12" stroke-linecap="round" fill="none"/>
          <circle cx="84" cy="34" r="9" fill="#7FB3D5"/>
        </svg>
        """;

    /// <summary>A sample logo as an .svg file in a temporary folder.</summary>
    public static string WriteLogo()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("usb-helper-report-").FullName, "logo.svg");
        File.WriteAllText(path, LogoSvg);
        return path;
    }

    /// <summary>The sample logo as a PNG with a transparent background, 512 pixels high.</summary>
    public static string WritePngLogo()
    {
        using var svg = new SKSvg();
        svg.FromSvg(LogoSvg);
        using var bitmap = new SKBitmap(new SKImageInfo(512, 512, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(512 / svg.Picture!.CullRect.Width);
            canvas.DrawPicture(svg.Picture);
        }
        var path = Path.Combine(Directory.CreateTempSubdirectory("usb-helper-report-").FullName, "logo-512.png");
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    /// <summary>A file with <paramref name="content"/> under <paramref name="name"/>, for logos that cannot be read.</summary>
    public static string WriteFile(string name, string content)
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("usb-helper-report-").FullName, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static ReportRow Row(int level, bool[] guides, bool isLast, string text, ConnectionStatus? status = null,
        string[]? badges = null, string? portType = null, string[]? notes = null) =>
        new(level, guides, isLast, text, status, badges ?? [], portType, notes ?? []);

    // Host > controller > root hub > a dock on a USB-C port with a camera, a speakerphone and a DisplayLink adapter, a
    // touch panel on an active cable, and the Displays group. Ports are rows of their own, as in the window.
    private static IReadOnlyList<ReportRow> DeskTree() =>
    [
        Row(0, [], true, "Dell Inc. Latitude 7450", notes: ["Hubs: 2 (USB2: 2, USB3: 1), deepest tier: 4, deepest hop: 3"]),
        Row(1, [], false, "Intel USB 3.20 eXtensible Host Controller", notes: ["Hubs: 2 (USB2: 2, USB3: 1), deepest tier: 4, deepest hop: 3"]),
        Row(2, [true], true, "USB Root Hub (USB 3.0)", notes: ["has a display"]),
        Row(3, [true, false], false, "Port 1", portType: "USB-C"),
        Row(4, [true, false, true], true, "Dell WD22TB4 dock", ConnectionStatus.Green, notes: ["has a display"]),
        Row(5, [true, false, true, false], false, "Port 1"),
        Row(6, [true, false, true, false, true], true, Device, ConnectionStatus.Green),
        Row(5, [true, false, true, false], false, "Port 2"),
        Row(6, [true, false, true, false, true], true, "Jabra Speak2 75", ConnectionStatus.Green),
        Row(5, [true, false, true, false], true, "Port 4"),
        Row(6, [true, false, true, false, false], true, "DisplayLink USB Graphics (17E9:6006)", ConnectionStatus.Orange, ["Display"],
            notes: ["Extended display: DisplayLink driver required"]),
        Row(3, [true, false], true, "Port 3"),
        Row(4, [true, false, false], true, "Crestron TSW-1070 touch panel", ConnectionStatus.Orange, ["+1 hops"]),
        Row(1, [], true, "Displays"),
        Row(2, [false], false, "Monitor (built-in)"),
        Row(2, [false], true, "DELL U2723QE (HDMI)"),
    ];

    private static IReadOnlyList<ReportProfile> Profiles() =>
    [
        new("Dell Latitude 7450", ConnectionStatus.Orange,
        [
            new("External hubs", 2, 2, LimitState.Within),
            new("Deepest tier", 5, 4, LimitState.Within),
            new("Most hops", 3, 3, LimitState.Within),
            new("External displays", 2, 1, LimitState.Within),
        ]),
        new("Windows on Intel", ConnectionStatus.Grey,
        [
            new("External hubs", null, 2, LimitState.Unknown),
            new("Deepest tier", 7, 4, LimitState.Within),
            new("Most hops", null, 3, LimitState.Unknown),
            new("External displays", null, 1, LimitState.Unknown),
        ]),
    ];

    private static IReadOnlyList<ReportFinding> Findings() =>
    [
        new(FactKind.DetectedFact, null, "Jabra Speak2 75", "Disconnected at 14:02 and reconnected on the same port."),
        new(FactKind.VerifiedDatabaseFact, "Dell Latitude 7450", "Computer", "At most 2 external displays on the computer's own outputs (verified)."),
        new(FactKind.PredictedRisk, "Dell Latitude 7450", "Crestron TSW-1070 touch panel",
            "3 hops reaches the recommended maximum of 3; an extra hub or active cable on this path may make it unstable."),
        new(FactKind.PredictedRisk, "Dell Latitude 7450", "DisplayLink USB Graphics",
            "A DisplayLink adapter needs its driver; without it the extended display stays dark."),
        new(FactKind.Unknown, "Windows on Intel", "Computer", "The hub limit of this platform is unknown."),
    ];

    public static ReportModel Full(string? logoPath) => new(
        Company, logoPath, Room, "0.9.0-beta.3", new DateTime(2026, 11, 13, 14, 5, 0),
        new ComputerIdentity("Dell Inc.", "Latitude 7450"), ConnectionStatus.Orange,
        new ReportCounts(2, 2, 1, 4, 3, false, 1), Profiles(),
        [new("Monitor", "built-in", null), new("DELL U2723QE", "HDMI", null), new("Monitor", "indirect, for example DisplayLink", "DisplayLink USB Graphics")],
        DeskTree(), Findings());

    public static ReportModel NoRoom(string? logoPath) => Full(logoPath) with { RoomName = null };

    public static ReportModel NoBranding() => Full(null) with { CompanyName = null };

    /// <summary>A tree of 200 devices: four controllers with five hubs of ten devices each, every device on its own port.</summary>
    public static ReportModel LongTree(string? logoPath)
    {
        var rows = new List<ReportRow> { Row(0, [], true, "Dell Inc. Precision 7680", notes: ["Hubs: 20, deepest tier: 3, deepest hop: 1"]) };
        for (var c = 1; c <= 4; c++)
        {
            var lastController = c == 4;
            rows.Add(Row(1, [], lastController, $"USB controller {c}"));
            rows.Add(Row(2, [!lastController], true, "USB Root Hub (USB 3.0)"));
            for (var h = 1; h <= 5; h++)
            {
                var lastHub = h == 5;
                rows.Add(Row(3, [!lastController, false], lastHub, $"Port {h}", portType: h == 1 ? "USB-C" : null));
                rows.Add(Row(4, [!lastController, false, !lastHub], true, $"Hub {c}.{h}", ConnectionStatus.Green));
                for (var d = 1; d <= 10; d++)
                {
                    rows.Add(Row(5, [!lastController, false, !lastHub, false], d == 10, $"Port {d}"));
                    rows.Add(Row(6, [!lastController, false, !lastHub, false, d != 10], true,
                        c == 1 && h == 1 && d == 1 ? Device : $"Test device {c}.{h}.{d}", d == 7 ? ConnectionStatus.Orange : ConnectionStatus.Green));
                }
            }
        }
        return Full(logoPath) with { Tree = rows, Counts = new ReportCounts(20, 20, 0, 3, 1, false, 1) };
    }

    /// <summary>The full report with a tree of <paramref name="devices"/> devices straight on the root hub: one row each.</summary>
    public static ReportModel Sized(int devices)
    {
        var rows = new List<ReportRow>
        {
            Row(0, [], true, "Dell Inc. Latitude 7450"),
            Row(1, [], true, "USB controller"),
            Row(2, [false], true, "USB Root Hub (USB 3.0)"),
        };
        rows.AddRange(Enumerable.Range(1, devices).Select(d => Row(3, [false, false], d == devices, $"Test device {d}", ConnectionStatus.Green)));
        return Full(null) with { Tree = rows };
    }
}
