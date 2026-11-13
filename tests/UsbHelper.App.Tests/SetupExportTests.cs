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

    // The known fake setup as the user left it: the computer profile checked, the dock's cable marked and a room entered.
    private static MainViewModel KnownSetup(EventLog? log = null)
    {
        var viewModel = NewViewModel(log);
        viewModel.RoomName = "Room 4.12";
        viewModel.Load(Desk());
        viewModel.Computers.Single().IsChecked = true;
        All(viewModel.Roots).Single(item => item.Id == "DOCK").ExtraHops = 1;
        return viewModel;
    }

    // Loads a setup.json as a fake detector would deliver it, with the same profiles checked, the same marks and room.
    private static MainViewModel LoadSetup(SetupFile setup)
    {
        var viewModel = new MainViewModel(DatabaseFolder.Load("Dell Inc.", "Latitude 7450", status: "verified"), [], setup.Computer,
            () => setup.Displays);
        viewModel.RoomName = setup.RoomName ?? "";
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
        Assert.Equal(TreeText.Render(viewModel.Roots[0], null, "Room 4.12", null), Text(files["tree.txt"]));
        Assert.Equal(Png, files["screenshot.png"]);
        Assert.Contains("Tree:", Text(files["log.txt"]));
        Assert.Contains(log.Entries, entry => entry.Text == "Setup exported");
    }

    // #186: exports on the same day must not overwrite each other; the room or system name tells them apart.
    [Fact]
    public void TheFileNameHasTheDateAndTimeAndNoRoomWhenNoneIsEntered()
    {
        var viewModel = NewViewModel();

        Assert.Equal("usb-helper_2026-11-03_18-02-41.zip", viewModel.SetupFileName(new DateTime(2026, 11, 3, 18, 2, 41)));
        viewModel.RoomName = "   ";
        Assert.Equal("usb-helper_2026-11-03_18-02-41.zip", viewModel.SetupFileName(new DateTime(2026, 11, 3, 18, 2, 41)));
    }

    [Fact]
    public void TheFileNameHasTheRoomOrSystemName()
    {
        var viewModel = NewViewModel();
        viewModel.RoomName = " Room 4.12 ";

        Assert.Equal("usb-helper_Room 4.12_2026-11-03_09-05-07.zip", viewModel.SetupFileName(new DateTime(2026, 11, 3, 9, 5, 7)));
    }

    [Fact]
    public void CharactersNotAllowedInFileNamesBecomeDashes()
    {
        var viewModel = NewViewModel();
        viewModel.RoomName = "B2/Lab:3*?\"<>|\t";

        Assert.Equal("usb-helper_B2-Lab-3-------_2026-11-03_09-05-07.zip", viewModel.SetupFileName(new DateTime(2026, 11, 3, 9, 5, 7)));
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

    // A computer name is often an asset or service tag, which is a serial number; matching needs only manufacturer and model.
    [Fact]
    public void SetupJsonLeavesOutTheComputerName()
    {
        var viewModel = new MainViewModel(null, [], new ComputerIdentity("Dell Inc.", "Latitude 7450", MachineName: "DESK-5CG1234XYZ"), Monitors);
        viewModel.Load(Desk());

        var json = Text(Export(viewModel)["setup.json"]);

        Assert.DoesNotContain("5CG1234XYZ", json);
        Assert.Null(SetupFile.FromJson(json).Computer.MachineName);
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

        Assert.Equal(Golden("golden-tree.txt"), Normalize(TreeText.Render(viewModel.Roots[0], setup.CompanyName, setup.RoomName, setup.LicensedTo)));
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
