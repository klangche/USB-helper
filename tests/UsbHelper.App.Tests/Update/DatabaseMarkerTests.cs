using UsbHelper.App.Update;

namespace UsbHelper.App.Tests.Update;

/// <summary>The database marker (#389): which database is in use, with a hover on where it came from.</summary>
public class DatabaseMarkerTests
{
    private static readonly DatabaseVersion BuiltIn = new(new string('a', 40), new DateTimeOffset(2026, 11, 24, 10, 0, 0, TimeSpan.Zero), BuiltIn: true);

    [Fact]
    public void TheMarkerNamesTheFormatTheDateAndTheCommit() =>
        Assert.Equal("Database v4 · 2026-11-24 (aaaaaaa)", BuiltIn.Marker(4));

    [Fact]
    public void TheHoverNamesTheFormatTheSourceTheCommitAndTheOtherFormats()
    {
        Assert.Equal($"Format: v4\nBuilt into this app\nCommit {new string('a', 40)}, 2026-11-24\nAlso built in, not used: v3", BuiltIn.MarkerDetail(4, [3]));
        Assert.StartsWith("Format: v4\nDownloaded from the database branch", (BuiltIn with { BuiltIn = false }).MarkerDetail(4, []), StringComparison.Ordinal);
    }

    [Fact]
    public void TheWindowShowsTheMarkerAndTheFormatWhenTheCommitIsUnknown()
    {
        var window = new ViewModels.MainViewModel(EmbeddedDatabaseTests.V3(), [], new Core.Detection.ComputerIdentity("", "", Platform: "windows-intel"))
        {
            DatabaseFormat = 3,
        };

        Assert.Equal("Database v3", window.DatabaseMarker);
        Assert.Null(window.DatabaseMarkerDetail);
    }
}
