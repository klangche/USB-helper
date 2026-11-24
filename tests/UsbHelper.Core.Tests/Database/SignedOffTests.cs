using UsbHelper.Core.Database;

namespace UsbHelper.Core.Tests.Database;

/// <summary>Signed-off, dated philosophy files (#389): the newest version of each id is used, older ones are kept.</summary>
public class SignedOffTests
{
    private const string SignOff = """

        signed off:
          by: Johan Niininen
          role: Product manager
          company: Ochno
          date: {0}
        """;

    private static string Ochno(string date, int max = 5) => $$"""
        name: Ochno
        selectable: true
        proav: 1
        own: true
        measures:
          chain:
            counts: [computer_hub, hub, marked_hub]
        rules:
          - { measure: chain, max: {{max}} }
        """ + string.Format(System.Globalization.CultureInfo.InvariantCulture, SignOff, date);

    private static Dictionary<string, string> Files(params (string File, string Yaml)[] guides)
    {
        var files = new Dictionary<string, string>
        {
            ["base.yaml"] = V4Tests.Base,
            ["platforms/windows.yaml"] = V4Tests.Windows,
            ["platforms/macos.yaml"] = V4Tests.MacOs,
            ["guides/usb-helper.yaml"] = V4Tests.UsbHelper,
        };
        foreach (var (file, yaml) in guides)
        {
            files[file] = yaml;
        }
        return files;
    }

    private static DatabaseError Error(params (string File, string Yaml)[] guides) =>
        Assert.Single(Assert.Throws<DatabaseException>(() => HardwareDatabase.LoadV4(Files(guides))).Errors);

    [Fact]
    public void TheNewestVersionIsUsedAndTheOlderIsKept()
    {
        var db = HardwareDatabase.LoadV4(Files(("guides/ochno-2026-11-19.yaml", Ochno("2026-11-19", max: 4)), ("guides/ochno-2026-11-24.yaml", Ochno("2026-11-24"))));

        var ochno = db.Guides["ochno"];
        Assert.Equal("guides/ochno-2026-11-24.yaml", ochno.File);
        Assert.Equal(5L, Assert.Single(ochno.Rules).Max);
        Assert.Equal(["guides/ochno-2026-11-19.yaml"], ochno.OlderFiles);
        Assert.Equal(new GuideSignOff("Johan Niininen", "Product manager", "Ochno", new DateOnly(2026, 11, 24)), ochno.SignedOff);
        Assert.Equal("Ochno: using guides/ochno-2026-11-24.yaml (signed off 2026-11-24 by Johan Niininen (Product manager, Ochno)); 1 older version kept: guides/ochno-2026-11-19.yaml",
            ochno.VersionLine);
    }

    // The id is the part before the date, so a remembered choice "ochno" still selects Ochno.
    [Fact]
    public void TheIdIsTheNameBeforeTheDate()
    {
        Assert.Equal(("ochno", new DateOnly(2026, 11, 24)), HardwareDatabase.GuideVersion("ochno-2026-11-24"));
        Assert.Equal(("usb-helper", (DateOnly?)null), HardwareDatabase.GuideVersion("usb-helper"));
        Assert.Equal(("ochno-2026-13-01", (DateOnly?)null), HardwareDatabase.GuideVersion("ochno-2026-13-01"));
        Assert.Contains("ochno", HardwareDatabase.LoadV4(Files(("guides/ochno-2026-11-24.yaml", Ochno("2026-11-24")))).Guides.Keys);
    }

    // The newest is used even when an undated file of the same id is still there.
    [Fact]
    public void AnUndatedFileIsOlderThanADatedOne()
    {
        var ochno = HardwareDatabase.LoadV4(Files(("guides/ochno.yaml", Ochno("2026-11-19", max: 4)), ("guides/ochno-2026-11-24.yaml", Ochno("2026-11-24")))).Guides["ochno"];

        Assert.Equal("guides/ochno-2026-11-24.yaml", ochno.File);
        Assert.Equal(["guides/ochno.yaml"], ochno.OlderFiles);
    }

    [Fact]
    public void ADatedFileWithoutASignOffIsAnError()
    {
        var error = Error(("guides/ochno-2026-11-24.yaml", Ochno("2026-11-24").Split("\nsigned off:")[0]));

        Assert.Equal("guides/ochno-2026-11-24.yaml", error.File);
        Assert.StartsWith("Field 'signed off' is required", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASignOffDateThatIsNotTheFileNamesDateIsAnErrorWithItsLine()
    {
        var error = Error(("guides/ochno-2026-11-24.yaml", Ochno("2026-11-23")));

        Assert.Equal(14, error.Line);
        Assert.Equal("Field 'signed off': 'date' 2026-11-23 must equal the date in the file name, 2026-11-24.", error.Message);
    }

    [Theory]
    [InlineData("  date: 2026-11-24", "  date: 24 Nov 2026", "Field 'signed off': 'date' is required, as YYYY-MM-DD.")]
    [InlineData("  by: Johan Niininen", "  by:", "Field 'signed off': 'by' is required: who approves this version.")]
    [InlineData("  role: Product manager", "  title: Product manager", "Field 'signed off': unknown field 'title'; it has by, role, company and date.")]
    public void ASignOffNeedsByAndAValidDate(string from, string to, string message) =>
        Assert.Equal(message, Error(("guides/ochno-2026-11-24.yaml", Ochno("2026-11-24").Replace(from, to, StringComparison.Ordinal))).Message);

    // Two versions with the same id and date differ only in case: one id spelled two ways is an error, dated or not.
    [Theory]
    [InlineData("guides/Ochno-2026-11-24.yaml", "guides/ochno-2026-11-24.yaml")]
    [InlineData("guides/Ochno.yaml", "guides/ochno.yaml")]
    [InlineData("guides/Ochno-2026-11-19.yaml", "guides/ochno-2026-11-24.yaml")]
    public void AnIdSpelledTwoWaysIsAnError(string first, string second)
    {
        var error = Error((first, Ochno(first[^15..^5])), (second, Ochno(second[^15..^5])));

        Assert.Equal($"The versions of 'ochno' spell its id in different ways: {first}, {second}. Use one spelling, in lower case.", error.Message);
    }

    // An older version is checked too: its error stops the load, as any file's does.
    [Fact]
    public void AnErrorInAnOlderVersionIsAnError()
    {
        var error = Error(("guides/ochno-2026-11-19.yaml", Ochno("2026-11-18")), ("guides/ochno-2026-11-24.yaml", Ochno("2026-11-24")));

        Assert.Equal("guides/ochno-2026-11-19.yaml", error.File);
    }

    [Fact]
    public void TheLogNamesTheVersionInUse()
    {
        var log = new UsbHelper.Core.Diagnostics.EventLog();

        log.DatabaseLoaded(HardwareDatabase.LoadV4(Files(("guides/ochno-2026-11-19.yaml", Ochno("2026-11-19")), ("guides/ochno-2026-11-24.yaml", Ochno("2026-11-24")))));

        Assert.Contains("Ochno: using guides/ochno-2026-11-24.yaml (signed off 2026-11-24 by Johan Niininen (Product manager, Ochno)); 1 older version kept: guides/ochno-2026-11-19.yaml",
            log.Entries.Select(entry => entry.Text));
    }

    // Until the database's files are renamed, an undated file without a sign-off still loads (#389).
    [Fact]
    public void AnUndatedFileMayHaveNoSignOff()
    {
        var ochno = HardwareDatabase.LoadV4(Files(("guides/ochno.yaml", Ochno("2026-11-24").Split("\nsigned off:")[0]))).Guides["ochno"];

        Assert.Null(ochno.SignedOff);
        Assert.Equal("Ochno: using guides/ochno.yaml", ochno.VersionLine);
    }
}
