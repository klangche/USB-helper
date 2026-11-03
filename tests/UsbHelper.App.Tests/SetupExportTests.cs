using System.IO.Compression;
using System.Text;
using UsbHelper.App.ViewModels;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;
using UsbHelper.Core.Export;

namespace UsbHelper.App.Tests;

// #135: File > Export full setup saves one zip with setup.json, tree.txt, screenshot.png and log.txt.
public class SetupExportTests
{
    private static readonly DateOnly Date = new(2026, 11, 3);
    private const string Version = "0.6.0-test";
    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G'];

    private static UsbNode Device(string id) => new(id, UsbNodeKind.Device, id, []);

    private static UsbNode Port(string hubId, int number, UsbNode? child) =>
        new($"{hubId}|port{number}", UsbNodeKind.Port, $"Port {number}", child == null ? [] : [child]) { PortNumber = number };

    private static UsbNode Hub(string id, params UsbNode?[] ports) =>
        new(id, UsbNodeKind.Hub, id, ports.Select((child, index) => Port(id, index + 1, child)).ToList());

    // A known fake setup: a dock on a USB-C port with a keyboard and a DisplayLink adapter, and a mouse.
    private static IReadOnlyList<UsbNode> Desk()
    {
        var root = Hub("ROOT", Hub("DOCK", Device("KEYBOARD") with { VendorId = 0x046D, ProductId = 0xC31C },
            Device("DL") with { VendorId = UsbNode.DisplayLinkVendorId, ProductId = 0x6006 }), Device(@"USB\VID_046D&PID_C077\SERIAL123") with { Name = "USB Mouse" });
        root = root with { Children = [root.Children[0] with { UsbC = true, UserConnectable = true }, root.Children[1] with { UserConnectable = true }] };
        return [new UsbNode("C1", UsbNodeKind.Controller, "Intel USB 3.2 controller", [root])];
    }

    private static IReadOnlyList<DisplayMonitor> Monitors() =>
        [new("PANEL", null, MonitorOutput.Internal), new("DELL", "DELL U2723QE", MonitorOutput.Hdmi)];

    private static MainViewModel NewViewModel(EventLog? log = null) =>
        new(DatabaseFolder.Load("Dell Inc.", "Latitude 7450", status: "verified"), [],
            new ComputerIdentity("Dell Inc.", "Latitude 7450", Platform: "p"), Monitors, log);

    // The known fake setup as the user left it: the computer profile checked and the dock's cable marked.
    private static MainViewModel KnownSetup(EventLog? log = null)
    {
        var viewModel = NewViewModel(log);
        viewModel.Load(Desk());
        viewModel.Computers.Single().IsChecked = true;
        All(viewModel.Roots).Single(item => item.Id == "DOCK").ExtraHops = 1;
        return viewModel;
    }

    // Loads a setup.json as a fake detector would deliver it, with the same profiles checked and the same marks.
    private static MainViewModel LoadSetup(SetupFile setup)
    {
        var viewModel = new MainViewModel(DatabaseFolder.Load("Dell Inc.", "Latitude 7450", status: "verified"), [], setup.Computer,
            () => setup.Displays);
        viewModel.Load(setup.Controllers);
        foreach (var option in viewModel.OperatingSystems.Concat(viewModel.Computers))
        {
            option.IsChecked = setup.CheckedProfiles.Contains(option.Profile.Id);
        }
        foreach (var (port, hops) in setup.Marks)
        {
            All(viewModel.Roots).Single(item => item.PortId == port).ExtraHops = hops;
        }
        return viewModel;
    }

    private static Dictionary<string, byte[]> Export(MainViewModel viewModel)
    {
        using var zip = new MemoryStream();
        viewModel.ExportSetup(zip, Png, Date, Version);
        zip.Position = 0;
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read);
        return archive.Entries.ToDictionary(entry => entry.FullName, entry =>
        {
            using var content = new MemoryStream();
            entry.Open().CopyTo(content);
            return content.ToArray();
        });
    }

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    [Fact]
    public void TheZipHasTheFourFiles()
    {
        var log = new EventLog();
        var viewModel = KnownSetup(log);

        var files = Export(viewModel);

        Assert.Equal(["setup.json", "tree.txt", "screenshot.png", "log.txt"], files.Keys);
        Assert.Equal(TreeText.Render(viewModel.Roots[0]), Text(files["tree.txt"]));
        Assert.Equal(Png, files["screenshot.png"]);
        Assert.Contains("Tree:", Text(files["log.txt"]));
        Assert.Contains(log.Entries, entry => entry.Text == "Setup exported");
    }

    [Fact]
    public void TheFileNameNamesTheComputerAndTheDate()
    {
        Assert.Equal("usb-helper-setup-Dell-Inc.-Latitude-7450-2026-11-03.zip", NewViewModel().SetupFileName(Date));
        Assert.Equal("usb-helper-setup-computer-2026-11-03.zip", new MainViewModel(new ComputerIdentity(null, null)).SetupFileName(Date));
    }

    [Fact]
    public void SetupJsonHoldsWhatWasFoundWithoutSerialNumbers()
    {
        var known = KnownSetup();
        var setup = SetupFile.FromJson(Text(Export(known)["setup.json"]));

        Assert.Equal(Version, setup.AppVersion);
        Assert.Equal(new ComputerIdentity("Dell Inc.", "Latitude 7450", Platform: "p"), setup.Computer);
        Assert.Equal(["p", known.Computers.Single().Profile.Id], setup.CheckedProfiles);
        Assert.Equal(Monitors().Select(monitor => (monitor.Name, monitor.Output)), setup.Displays!.Select(monitor => (monitor.Name, monitor.Output)));
        Assert.Single(setup.Marks);
        Assert.Equal(2, setup.Results.Count);
        Assert.Contains(setup.Results.SelectMany(result => result.Findings), finding => finding.Kind == FactKind.Unknown);
        Assert.DoesNotContain("SERIAL123", Text(Export(KnownSetup())["setup.json"]));
    }

    [Fact]
    public void SetupJsonRoundTripsIntoATreeThatEvaluatesTheSame()
    {
        var exported = SetupFile.FromJson(Text(Export(KnownSetup())["setup.json"]));

        var again = SetupFile.FromJson(Text(Export(LoadSetup(exported))["setup.json"]));

        Assert.Equal(exported.ToJson(), again.ToJson());
    }

    // A reported setup becomes a test case: its setup.json is loaded as fake detector input.
    [Fact]
    public void AReportedSetupLoadsAsATestCase()
    {
        var setup = SetupFile.FromJson(Golden("golden-setup.json"));

        var viewModel = LoadSetup(setup);

        Assert.Equal(Golden("golden-tree.txt"), Normalize(TreeText.Render(viewModel.Roots[0])));
    }

    [Fact]
    public void AnExportOfTheKnownSetupMatchesTheGoldenFiles()
    {
        var files = Export(KnownSetup());

        Assert.Equal(Golden("golden-setup.json"), Normalize(Text(files["setup.json"])));
        Assert.Equal(Golden("golden-tree.txt"), Normalize(Text(files["tree.txt"])));
    }

    [Fact]
    public void ExportSetupIsOnlyOfferedOnceTheTreeIsRead()
    {
        var viewModel = NewViewModel();
        Assert.False(viewModel.ExportSetupCommand.CanExecute(null));

        viewModel.Load(Desk());

        Assert.True(viewModel.ExportSetupCommand.CanExecute(null));
    }

    // Git may check the golden files out with CRLF on Windows.
    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');

    private static string Golden(string name) => Normalize(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Setup", name)));

    private static IEnumerable<NodeViewModel> All(IEnumerable<NodeViewModel> items) => items.SelectMany(item => All(item.Children).Prepend(item));
}
