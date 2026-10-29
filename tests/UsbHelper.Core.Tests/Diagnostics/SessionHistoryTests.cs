using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;
using static UsbHelper.Core.Tests.Detection.TestTree;

namespace UsbHelper.Core.Tests.Diagnostics;

public class SessionHistoryTests
{
    private static readonly IReadOnlyList<UsbNode> WithHub = [Controller("C1", Hub("ROOT", Hub("HUB", Device("KEYBOARD")), Device("MOUSE")))];
    private static readonly IReadOnlyList<UsbNode> HubUnplugged = [Controller("C1", Hub("ROOT", null, Device("MOUSE")))];

    [Fact]
    public void NothingIsMissingWhileEverythingIsConnected()
    {
        var history = new SessionHistory();
        history.Record(WithHub);

        Assert.Empty(history.Missing(WithHub));
    }

    [Fact]
    public void UnpluggedHubAndWhatWasBehindItAreMissing()
    {
        var history = new SessionHistory();
        history.Record(WithHub);

        var missing = history.Missing(HubUnplugged);

        Assert.Equal(["HUB", "KEYBOARD"], missing.Select(device => device.Node.Id).Order());
        Assert.Equal("Port 1 of HUB", missing.Single(device => device.Node.Id == "KEYBOARD").PortName);
    }

    [Fact]
    public void DeviceThatReturnsIsNoLongerMissing()
    {
        var history = new SessionHistory();
        history.Record(WithHub);
        history.Record(HubUnplugged);

        Assert.Empty(history.Missing(WithHub));
    }

    [Fact]
    public void ClearedDeviceIsNoLongerReported()
    {
        var history = new SessionHistory();
        history.Record(WithHub);

        history.Clear("KEYBOARD");

        Assert.Equal(["HUB"], history.Missing(HubUnplugged).Select(device => device.Node.Id));
    }

    [Fact]
    public void PlaceholderForUnidentifiedDeviceIsNotRemembered()
    {
        IReadOnlyList<UsbNode> failed = [Controller("C1", Hub("ROOT", Device("ROOT|port1|device", "The device failed enumeration.")))];
        IReadOnlyList<UsbNode> identified = [Controller("C1", Hub("ROOT", Device("MOUSE")))];
        var history = new SessionHistory();
        history.Record(failed);

        Assert.Empty(history.Missing(identified));
    }

    [Fact]
    public void MissingDeviceKnowsWhatItWasBehind()
    {
        var history = new SessionHistory();
        history.Record(WithHub);

        var keyboard = history.Missing(HubUnplugged).Single(device => device.Node.Id == "KEYBOARD");

        Assert.Equal(["C1", "ROOT", "ROOT|port1", "HUB", "HUB|port1"], keyboard.AncestorIds);
    }

    [Fact]
    public void DeviceMovedToAnotherPortRemembersItsNewPort()
    {
        IReadOnlyList<UsbNode> moved = [Controller("C1", Hub("ROOT", Hub("HUB", Device("KEYBOARD")), null, Device("MOUSE")))];
        IReadOnlyList<UsbNode> gone = [Controller("C1", Hub("ROOT", Hub("HUB", Device("KEYBOARD")), null, null))];
        var history = new SessionHistory();
        history.Record(WithHub);
        history.Record(moved);

        Assert.Equal("ROOT|port3", Assert.Single(history.Missing(gone)).PortId);
    }
}
