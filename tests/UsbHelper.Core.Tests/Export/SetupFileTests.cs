using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;
using UsbHelper.Core.Export;
using static UsbHelper.Core.Tests.Detection.TestTree;

namespace UsbHelper.Core.Tests.Export;

// #135: setup.json, everything detected, readable and loadable again as test input.
public class SetupFileTests
{
    // Windows device paths carry serial numbers; the export must not.
    private const string Serial = "4C530001234567";

    private static IReadOnlyList<UsbNode> Desk()
    {
        var stick = Device($@"USB\VID_0781&PID_5567\{Serial}") with { Name = "Cruzer", VendorId = 0x0781, ProductId = 0x5567 };
        var root = Hub("ROOT", Hub("HUB", stick, null), null) with { Name = "Root hub" };
        root = root with { Children = [root.Children[0] with { UsbC = true, UserConnectable = true }, root.Children[1]] };
        return [Controller("PCI\\VEN_8086", root) with { Name = "Intel USB 3.2" }];
    }

    [Fact]
    public void IdsArePositionsInTheTreeWithoutSerialNumbers()
    {
        var (tree, marks) = SetupFile.Anonymize(Desk(), new Dictionary<string, int> { ["ROOT|port1"] = 2 });

        var all = Flatten(tree).ToList();
        Assert.Equal(["1", "1/1", "1/1/1", "1/1/1/1", "1/1/1/1/1", "1/1/1/1/1/1", "1/1/1/1/2", "1/1/2"], all.Select(node => node.Id));
        Assert.DoesNotContain(all, node => node.Id.Contains(Serial, StringComparison.Ordinal));
        Assert.Equal("Cruzer", all[5].Name); // Everything else is kept.
        Assert.Equal((ushort)0x0781, all[5].VendorId);
        Assert.True(all[2].UsbC);
        Assert.Equal(new Dictionary<string, int> { ["1/1/1"] = 2 }, marks);
    }

    [Fact]
    public void AnUnidentifiedDeviceKeepsItsPlaceholderId()
    {
        // An id that starts with its port's id and "|" stands for a hub or device Windows could not identify.
        IReadOnlyList<UsbNode> tree = [Controller("C1", Hub("ROOT", Device("ROOT|port1|device") with { Problem = "code 43" }))];

        var (anonymous, _) = SetupFile.Anonymize(tree, new Dictionary<string, int>());

        Assert.Equal("1/1/1|device", Flatten(anonymous).Last().Id);
    }

    // #193: a companion port id is a Windows hub id, which can hold a serial number; it gets the companion's position.
    [Fact]
    public void CompanionPortIdsGetTheCompanionsPositionAndTheBusIsKept()
    {
        var root = Hub("ROOT", null, null);
        root = root with
        {
            Children =
            [
                root.Children[0] with { Bus = UsbBus.Usb2, CompanionPortId = "ROOT|port2" },
                root.Children[1] with { Bus = UsbBus.Usb3, CompanionPortId = $@"USB\VID_1234\{Serial}|port4" },
            ],
        };

        var (anonymous, _) = SetupFile.Anonymize([Controller("C1", root)], new Dictionary<string, int>());

        var ports = Flatten(anonymous).Where(node => node.Kind == UsbNodeKind.Port).ToList();
        Assert.Equal("1/1/2", ports[0].CompanionPortId);
        Assert.Equal(UsbBus.Usb2, ports[0].Bus);
        Assert.Null(ports[1].CompanionPortId); // Not in the tree: dropped, never exported as it was.
        Assert.Equal(UsbBus.Usb3, ports[1].Bus);
    }

    // Container ids (#143) identify physical devices; the export keeps only which ones are equal.
    [Fact]
    public void ContainerIdsAreReplacedButStillLink()
    {
        var samsung = Guid.NewGuid();
        IReadOnlyList<UsbNode> tree = [Controller("C1", Hub("ROOT",
            Hub("MONITOR-HUB", Device("CAM") with { ContainerId = samsung }) with { ContainerId = samsung },
            Device("WEBCAM") with { ContainerId = MonitorPlacement.ThisComputer }))];
        IReadOnlyList<DisplayMonitor> monitors =
            [new("M1", "LS27A600U", MonitorOutput.DisplayPort) { ContainerId = samsung }, new("M2", "Panel", MonitorOutput.Internal)];

        var (anonymousTree, anonymousMonitors) = SetupFile.AnonymizeContainers(tree, monitors);

        var nodes = Flatten(anonymousTree).ToList();
        var replaced = nodes.Single(node => node.Id == "MONITOR-HUB").ContainerId;
        Assert.NotEqual(samsung, replaced);
        Assert.NotNull(replaced);
        Assert.Equal(replaced, nodes.Single(node => node.Id == "CAM").ContainerId);
        Assert.Equal(MonitorPlacement.ThisComputer, nodes.Single(node => node.Id == "WEBCAM").ContainerId); // Well known, kept.
        Assert.Equal(replaced, anonymousMonitors![0].ContainerId);
        Assert.Null(anonymousMonitors[1].ContainerId);
        Assert.Equal(["MONITOR-HUB"], MonitorPlacement.Place(anonymousTree, anonymousMonitors).Linked.Keys);
    }

    [Fact]
    public void TheJsonRoundTrips()
    {
        var (tree, marks) = SetupFile.Anonymize(Desk(), new Dictionary<string, int> { ["ROOT|port1"] = 2 });
        var setup = new SetupFile(
            AppVersion: "0.6.0-beta.3",
            Date: new DateOnly(2026, 11, 3),
            Computer: new ComputerIdentity("Dell Inc.", "Latitude 7450", "Intel Core Ultra 7", "windows-intel", "DESK-1"),
            CheckedProfiles: ["windows-intel"],
            Controllers: tree,
            Marks: marks,
            Displays: [new DisplayMonitor("1", "DELL U2723QE", MonitorOutput.Hdmi)],
            Results: [new SetupResult("windows-intel", "Windows (Intel)", ConnectionStatus.Purple,
                [new SetupFinding(FactKind.Unknown, "This computer", "limits.max_hubs is unknown.")])]);

        var json = setup.ToJson();
        var read = SetupFile.FromJson(json);

        Assert.Contains("\"format_version\": 1", json);
        Assert.Contains("\"output\": \"hdmi\"", json); // Readable words, not numbers.
        Assert.Contains("\"kind\": \"device\"", json);
        Assert.Contains("\"vendor_id\": \"0781\"", json);
        Assert.DoesNotContain("is_display_link", json); // Worked out from the vendor id, not stored.
        Assert.Equal(json, read.ToJson());
        Assert.Equal(setup.Computer, read.Computer);
        Assert.Equal(Flatten(setup.Controllers).Select(node => node with { Children = [] }), Flatten(read.Controllers).Select(node => node with { Children = [] }));
        Assert.Equal(setup.Marks, read.Marks);
    }

    [Fact]
    public void ANewerFormatIsRefused()
    {
        Assert.Throws<NotSupportedException>(() => SetupFile.FromJson("{\"format_version\": 2}"));
        Assert.Throws<NotSupportedException>(() => SetupFile.FromJson("{\"format_version\": \"one\"}"));
        Assert.Throws<NotSupportedException>(() => SetupFile.FromJson("[]"));
    }

    private static IEnumerable<UsbNode> Flatten(IEnumerable<UsbNode> nodes) => nodes.SelectMany(node => Flatten(node.Children).Prepend(node));
}
