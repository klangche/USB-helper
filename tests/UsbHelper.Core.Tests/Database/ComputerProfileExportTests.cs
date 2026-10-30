using UsbHelper.Core.Database;
using UsbHelper.Core.Detection;
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
        foreach (var field in new[] { "limits.max_hubs", "limits.max_tiers", "limits.max_hops", "displays.max_external" })
        {
            Assert.Equal(ValueKind.Null, computer.Value.Find(field)!.Kind);
        }
    }

    [Fact]
    public void NotesSayWhatWasDetected()
    {
        using var db = new TestDatabase().Write("computers/export.yaml", Export(Computer));

        var notes = db.Load().Computers.Values.Single().Find("notes")!.Items.Select(item => (string)item.Scalar!).ToList();

        Assert.Contains("Exported by USB-helper 1.2.3 on 2026-10-30.", notes);
        Assert.Contains("Detected: USB controllers: 2, external hubs: 1, active external displays: 2.", notes);
        Assert.Contains("USB controller C1: root hub ports: 3.", notes);
        Assert.Contains("USB controller C2: root hub ports: 1.", notes);
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

        Assert.Contains("inherits: null", yaml);
        Assert.Contains("# Choose 'inherits' from database/platforms/.", yaml);
        using var db = new TestDatabase().Write("computers/export.yaml", yaml);
        Assert.Contains("'inherits' is required", Assert.Single(db.LoadErrors()).Message);
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
