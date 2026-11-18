using UsbHelper.Core.Database;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;
using static UsbHelper.Core.Tests.Detection.TestTree;

namespace UsbHelper.Core.Tests.Database;

public class ComputerProfileExportTests
{
    private static readonly ComputerIdentity Computer = new("Dell Inc.", "Latitude \"7450\"", "Intel(R) Core(TM) Ultra 7 165U", "test-platform");

    // Two controllers; the first has a hub with a keyboard.
    private static readonly IReadOnlyList<UsbNode> Tree =
        [Controller("C1", Hub("ROOT1", Hub("HUB", Device("KEYBOARD")), null, null)), Controller("C2", Hub("ROOT2", (UsbNode?)null))];

    private static string Export(ComputerIdentity computer, int? displays = 2) =>
        ComputerProfileExport.ToYaml(computer, Tree, displays, "1.2.3", new DateOnly(2026, 10, 30));

    [Fact]
    public void ExportLoadsAsAnUnverifiedComputerWithTheComputedId()
    {
        using var db = new TestDatabase().Write("computers/export.yaml", Export(Computer));

        var computer = Assert.Single(db.Load().Computers);

        Assert.Equal(ComputerProfileId.From("Dell Inc.", "Latitude \"7450\""), computer.Key);
        Assert.Equal("Dell Inc.", computer.Value.Find("manufacturer")!.Scalar);
        Assert.Equal("Latitude \"7450\"", computer.Value.Find("model")!.Scalar);
        Assert.Equal("Intel(R) Core(TM) Ultra 7 165U", computer.Value.Find("chip")!.Scalar);
        Assert.Equal("unverified", computer.Value.Find("verification.status")!.Scalar);
        Assert.Equal(ValueKind.Null, computer.Value.Find("displays.max_external")!.Kind);
        Assert.Equal("test", computer.Value.Platform);
        Assert.Equal("platform", computer.Value.Chip);
    }

    [Fact]
    public void ExportInheritsTheChipsFacts()
    {
        using var db = new TestDatabase()
            .Write("base.yaml", TestDatabase.Base.Replace("unverified", "partial"))
            .Write(TestDatabase.PlatformFile, TestDatabase.Platform.Replace("built_in_hub_levels: 0", "built_in_hub_levels: 1") + "\nverification:\n  status: partial\n")
            .Write("computers/export.yaml", Export(Computer));

        var computer = db.Load().Computers.Values.Single();

        Assert.DoesNotContain("limits:", Export(Computer));
        Assert.DoesNotContain("built_in_hub_levels", Export(Computer));
        Assert.Equal(1L, computer.Find("built_in_hub_levels")!.Scalar);
        Assert.Equal(TestDatabase.PlatformFile, computer.Find("built_in_hub_levels")!.File);
        // Tiers count the built-in hub levels: the keyboard behind one hub is on tier 4.
        Assert.Equal(4, DiagnosticEvaluator.Evaluate(Tree, computer, [], externalDisplays: 0).Tiers.Actual);
    }

    [Fact]
    public void NotesSayWhatWasDetected()
    {
        using var db = new TestDatabase().Write("computers/export.yaml", Export(Computer));

        var notes = db.Load().Computers.Values.Single().Find("notes")!.Items.Select(item => (string)item.Scalar!).ToList();

        Assert.Contains("Exported by USB-helper 1.2.3 on 2026-10-30.", notes);
        Assert.Contains("Detected: USB controllers: 2, external hubs: 1, active external displays: 2.", notes);
        Assert.Contains("USB controller 1: root hub ports: 3.", notes);
        Assert.Contains("USB controller 2: root hub ports: 1.", notes);
    }

    // Windows names controllers in the user's language; the notes are for the developers and stay in English.
    [Fact]
    public void ControllersAreLabelledInEnglishNotByTheirWindowsName()
    {
        var swedish = new UsbNode("C1", UsbNodeKind.Controller, "USB-styrenhet (Universal Serial Bus)", [Hub("ROOT1", null, null)]);

        var yaml = ComputerProfileExport.ToYaml(Computer, [swedish], 0, "1.2.3", new DateOnly(2026, 10, 31));

        Assert.Contains("USB controller 1: root hub ports: 2.", yaml);
        Assert.DoesNotContain("styrenhet", yaml);
    }

    [Fact]
    public void UnknownValuesAreSaidToBeUnknown()
    {
        var yaml = Export(Computer with { Chip = null }, displays: null);

        Assert.Contains("chip: null", yaml);
        Assert.Contains("active external displays: unknown.", yaml);
    }

    [Fact]
    public void UnknownPlatformIsLeftForTheDeveloperToChoose()
    {
        var yaml = Export(Computer with { Platform = null });

        Assert.Contains("platform: null", yaml);
        Assert.Contains("chip_family: null", yaml);
        Assert.Contains("# Choose 'platform' (a file in v3/platforms/ on the database branch) and 'chip_family' (one of its chips).", yaml);
        using var db = new TestDatabase().Write("computers/export.yaml", yaml);
        Assert.Equal(["Field 'platform' is required and must be text.", "Field 'chip_family' is required and must be text."],
            db.LoadErrors().Select(error => error.Message));
    }

    // Reported names come from firmware and drivers: any character must still give a file that loads unchanged.
    [Theory]
    [InlineData("a\nb")]
    [InlineData("a\r\nb")]
    [InlineData("a\0b")]
    [InlineData("a\u0085b")]
    [InlineData("a\u2028b\u2029c")]
    [InlineData("\uFEFFa\tb\u007F")]
    [InlineData("C:\\path\\ \"quoted\" # not a comment: 'x'")]
    [InlineData(" spaces  around ")]
    public void AnyReportedTextLoadsUnchanged(string text)
    {
        using var db = new TestDatabase().Write("computers/export.yaml", Export(Computer with { Model = text, Chip = text }));

        var computer = db.Load().Computers.Values.Single();

        Assert.Equal(text, computer.Find("model")!.Scalar);
        Assert.Equal(text, computer.Find("chip")!.Scalar);
    }

    [Theory]
    [InlineData("Dell Inc.", "Latitude 7450", "Dell-Inc.-Latitude-7450.yaml")]
    [InlineData("LENOVO", "21ML/0035:MX", "LENOVO-21ML-0035-MX.yaml")]
    [InlineData("Apple Inc.", "MacBookPro18,2", "Apple-Inc.-MacBookPro18-2.yaml")]
    [InlineData("!!", "??", "computer.yaml")]
    public void FileNameIsManufacturerAndModel(string manufacturer, string model, string fileName)
    {
        Assert.Equal(fileName, ComputerProfileExport.FileName(new ComputerIdentity(manufacturer, model)));
    }
}
