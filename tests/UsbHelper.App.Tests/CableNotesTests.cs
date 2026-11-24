using UsbHelper.App.ViewModels;
using UsbHelper.Core.Database;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;
using UsbHelper.Core.Export;
using UsbHelper.Core.Topology;

namespace UsbHelper.App.Tests;

// #372: cable lengths are notes on a marked cable's hover and in the report, from the selected philosophy; never a status.
public class CableNotesTests
{
    // Test guides in the format of #372, with the owner's cables from the issue; the real ones come with the database.
    private const string PureLink = """
        name: PureLink
        selectable: true
        proav: 0
        sources:
          v6: { title: "PureLink V6" }
          pi6600: { link: "https://www.purelink.de/en/connection-cables/usb/21002/premium-usb-3.2-gen1-micro-coax-usb-c-cable-5.00m" }
        measures:
          tiers: { counts: [root_hub, computer_hub, hub, marked_hub, cable_length, device], computer_hub: { from: [detected], unknown: open } }
        rules:
          - { measure: tiers, recommended: 4, max: 7 }
        cables:
          notes:
            - { type: passive, bus: usb3, text: "passive USB 3.2 Gen 1 about 2 m (indicative)", source: v6, quote: "USB 3.2 Gen 1 ~2 m" }
          models:
            - { name: "PureInstall PI6600 (21002)", type: passive, length: 5, speed: "USB 3.2 Gen 1", source: pi6600 }
        """;

    private const string Ochno = """
        name: Ochno
        selectable: true
        proav: 0
        measures:
          chain: { counts: [computer_hub, hub, marked_hub], computer_hub: { from: [detected], unknown: open } }
        rules:
          - { measure: chain, max: 5 }
        cables:
          models:
            - { name: O-EC-10, type: active_optical, length: 10, speed: "USB 3.2 Gen 2", hubs: 0, tiers: 0, host: false }
            - { name: O-EC-20, type: active_optical, length: 20, bus: usb3, speed: "USB 3.2 Gen 2", hubs: 0, tiers: 0, host: false }
        """;

    private static HardwareDatabase Database(params (string Id, string Text)[] guides)
    {
        var files = new Dictionary<string, string>(EmbeddedDatabase.Files()[4]);
        foreach (var (id, text) in guides)
        {
            files[$"guides/{id}.yaml"] = text;
        }
        return DatabaseFormats.Read(4, files);
    }

    private static UsbNode Device(string id) => new(id, UsbNodeKind.Device, id, []);

    private static UsbNode Port(string id, int number, UsbBus bus, UsbNode child) =>
        new(id, UsbNodeKind.Port, $"Port {number}", [child]) { PortNumber = number, Bus = bus, UserConnectable = true };

    // A computer port with a dock, and a second dock on the dock's port 1 (D5: the cable between them).
    private static IReadOnlyList<UsbNode> Room =>
    [
        new("C1", UsbNodeKind.Controller, "Controller", [new UsbNode("ROOT", UsbNodeKind.Hub, "Root", [
            Port("ROOT|p1", 1, UsbBus.Usb3, new UsbNode("DOCK", UsbNodeKind.Hub, "Dock", [
                Port("DOCK|p1", 1, UsbBus.Usb3, new UsbNode("DOCK2", UsbNodeKind.Hub, "Second dock", [Port("DOCK2|p1", 1, UsbBus.Usb3, Device("CAMERA"))]))]))])]),
    ];

    private static MainViewModel Loaded(HardwareDatabase db, string guide)
    {
        var viewModel = new MainViewModel(db, [], new ComputerIdentity("Maker", "Model", Platform: "windows-intel"), () => [], new EventLog())
        {
            Guide = db.Guides[guide],
        };
        viewModel.Load(Room);
        return viewModel;
    }

    private static NodeViewModel Row(MainViewModel viewModel, string id) => All(viewModel.Roots[0]).Single(row => row.Id == id);

    private static IEnumerable<NodeViewModel> All(NodeViewModel row) => row.Children.SelectMany(All).Prepend(row);

    private static ConnectionStatus? StatusOf(MainViewModel viewModel, string id) => Row(viewModel, id).Status;

    [Fact]
    public void APassiveHostCableUnderPureLinkShowsTheTwoMetreNoteAndTheApprovedModelAndNoStatusChange()
    {
        var viewModel = Loaded(Database(("purelink", PureLink)), "purelink");
        var dock = Row(viewModel, "DOCK");
        var before = (StatusOf(viewModel, "DOCK"), StatusOf(viewModel, "CAMERA"));

        dock.CableMetres = 3;
        dock.CableBusIndex = 1;
        dock.CableKindIndex = 0; // passive
        dock.SetCable.Execute(null);

        Assert.True(dock.Mark!.Host); // On a computer port.
        Assert.Equal(["PureLink: passive USB 3.2 Gen 1 about 2 m (indicative); PureInstall PI6600 (21002) is rated 5 m"], dock.CableNoteLines);
        Assert.Contains("PureLink: passive USB 3.2 Gen 1 about 2 m", dock.RowTip);
        Assert.Equal("3 m USB 3 passive cable", dock.MarkBadge); // The badge names the type.
        Assert.Equal(before, (StatusOf(viewModel, "DOCK"), StatusOf(viewModel, "CAMERA")));

        // Marked as the approved model: its rated 5 m and its speed; still no status change, and no tier for a passive cable.
        dock.CableModelIndex = 1;
        dock.SetCable.Execute(null);

        Assert.Equal((5.0, "PureInstall PI6600 (21002)", "passive"), (dock.Mark!.Metres!.Value, dock.Mark.Model!, dock.Mark.Kind!));
        Assert.Contains("PureLink PureInstall PI6600 (21002): 5 m passive, USB 3.2 Gen 1", dock.CableNoteLines);
        Assert.Equal(before, (StatusOf(viewModel, "DOCK"), StatusOf(viewModel, "CAMERA")));
        // The notes are in the report's rows too.
        Assert.Contains(viewModel.Report(DateTime.Now, null, null, "0").Tree, row => row.Notes.Any(note => note.StartsWith("PureLink: passive", StringComparison.Ordinal)));
    }

    [Fact]
    public void AnOchnoExtensionCableAsTheHostCableIsNotAllowedAndAddsNothingOnADockPort()
    {
        var viewModel = Loaded(Database(("ochno", Ochno)), "ochno");
        var dock = Row(viewModel, "DOCK");
        var before = StatusOf(viewModel, "CAMERA");

        dock.CableModelIndex = 1; // O-EC-10 on the computer port.
        dock.SetCable.Execute(null);

        Assert.Contains("Ochno: O-EC-10 is not allowed as the host cable.", dock.CableNoteLines);
        Assert.Equal(before, StatusOf(viewModel, "CAMERA"));

        // O-EC-20 on the dock's port: 0 hubs and 0 tiers, its 20 m rating, and no host-cable note.
        var second = Row(viewModel, "DOCK2");
        second.CableModelIndex = 2;
        second.SetCable.Execute(null);

        Assert.Equal((0, 0, false, UsbBus.Usb3), (second.Mark!.Hubs, second.Mark.Tiers, second.Mark.Host, second.Mark.Bus)); // The model's bus.
        Assert.Equal(["Ochno O-EC-20: 20 m active optical, USB 3.2 Gen 2, adds no hubs or tiers"], second.CableNoteLines);
        Assert.Equal(before, StatusOf(viewModel, "CAMERA"));
    }

    // D5: the cable between daisy-chained docks, marked as a host cable, gets the host-cable notes too.
    [Fact]
    public void TheCableBetweenDocksMarkedAsAHostCableIsNotedLikeOne()
    {
        var viewModel = Loaded(Database(("ochno", Ochno)), "ochno");
        var second = Row(viewModel, "DOCK2");

        second.CableModelIndex = 1;
        second.CableHost = true;
        second.SetCable.Execute(null);

        Assert.Contains("Ochno: O-EC-10 is not allowed as the host cable.", second.CableNoteLines);
    }

    [Fact]
    public void APhilosophyWithoutCableNotesShowsNone()
    {
        // The general-purpose guide states nothing about cables.
        var db = Database();
        var viewModel = Loaded(db, "desktop");
        var dock = Row(viewModel, "DOCK");

        dock.CableMetres = 6;
        dock.SetCable.Execute(null);

        Assert.Empty(dock.CableNoteLines);
        Assert.Empty(db.Guides["desktop"].CableNotes);
    }

    [Fact]
    public void TheCableMarksSurviveSaveAndLoad()
    {
        var mark = CableMark.ActiveCable(10, UsbBus.Usb3) with { Kind = "active_optical", Model = "O-EC-10", Host = true };
        var file = new SetupFile("0", new DateOnly(2026, 11, 24), new ComputerIdentity(null, null), [], Room,
            new Dictionary<string, CableMark> { ["ROOT|p1"] = mark }, null, []);

        Assert.Equal(mark, SetupFile.FromJson(file.ToJson()).Marks["ROOT|p1"]);
    }

    // A guide's cables section is checked like the rest of the file.
    [Fact]
    public void AWrongCableEntryIsADatabaseError()
    {
        var bad = Ochno.Replace("type: active_optical, length: 10", "type: fibre, length: 0", StringComparison.Ordinal);

        var errors = Assert.Throws<DatabaseException>(() => Database(("ochno", bad))).Errors;

        Assert.Contains(errors, error => error.Message.Contains("'type' must be one of", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Message.Contains("'length' is required", StringComparison.Ordinal));
    }

    // A passive cable adds no tiers, whatever its length; an active one still does where the philosophy converts length (#331).
    [Fact]
    public void APassiveCableAddsNoTiersAndAnActiveOneStillDoes()
    {
        var viewModel = Loaded(Database(), "crestron");
        var dock = Row(viewModel, "DOCK");
        var before = StatusOf(viewModel, "CAMERA");

        dock.CableMetres = 40;
        dock.CableKindIndex = 0; // passive
        dock.SetCable.Execute(null);
        Assert.Equal(before, StatusOf(viewModel, "CAMERA"));

        dock.CableKindIndex = 1; // active copper
        dock.SetCable.Execute(null);
        Assert.True(StatusOf(viewModel, "CAMERA") > before);
    }
}
