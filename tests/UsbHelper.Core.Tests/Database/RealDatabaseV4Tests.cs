using UsbHelper.Core.Database;

namespace UsbHelper.Core.Tests.Database;

/// <summary>The repository's v4 database (#328), from database/v4/ after release/database.sh.</summary>
public class RealDatabaseV4Tests
{
    // The sources a computer_hubs value may name (#325).
    private static readonly string[] SourceNames = ["Ochno", "Lightware", "PureLink", "Extron", "Crestron", "USB 2.0 specification"];

    internal static string Folder() => Path.Combine(Path.GetDirectoryName(RealDatabaseTests.DatabaseFolder())!, "v4");

    private static Dictionary<string, string> Files() =>
        Directory.EnumerateFiles(Folder(), "*.yaml", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(Folder(), path).Replace('\\', '/'), File.ReadAllText);

    [Fact]
    public void TheWholeV4DatabaseLoads()
    {
        var db = HardwareDatabase.LoadV4(Files());

        Assert.Equal(4, db.Format);
        Assert.NotEmpty(db.Platforms);
        Assert.NotEmpty(db.Computers);
        Assert.Contains(Guide.DefaultId, db.Guides.Keys);
    }

    // Evidence-based computer hubs (#325): M-series 1, the MacBook Neo 2, every other chip unknown.
    [Fact]
    public void ComputerHubsAreSetOnlyWhereASourceEstablishesThem()
    {
        var db = HardwareDatabase.LoadV4(Files());

        var set = db.Platforms.Values.Where(profile => profile.Find("computer_hubs")?.Scalar != null).Select(profile => profile.Id);
        Assert.Equal(["macos-apple-silicon"], set);
        Assert.Equal(1L, db.Platforms["macos-apple-silicon"].Find("computer_hubs")!.Scalar);
        Assert.Equal(2L, db.Computers[ComputerProfileId.From("Apple Inc.", "Mac17,5")].Find("computer_hubs")!.Scalar);
    }

    // Each value has a note that starts with the value and names its source.
    [Fact]
    public void EveryComputerHubsValueHasANoteNamingItsSource()
    {
        var db = HardwareDatabase.LoadV4(Files());

        foreach (var profile in db.Platforms.Values.Concat(db.Computers.Values))
        {
            var value = profile.Find("computer_hubs");
            if (value == null || value.Kind == ValueKind.Null)
            {
                continue;
            }
            // A number, or one per bus: the note starts with the number, or with the first bus's.
            var hubs = value.Scalar as long? ?? value.Fields.Values.Select(bus => bus.Scalar as long?).First();
            var fromComputer = value.File.StartsWith("computers/", StringComparison.Ordinal);
            var start = fromComputer ? $"computer_hubs {hubs}:" : $"{profile.Chip}, computer_hubs {hubs}:";
            var notes = db.Platforms.Values.Concat(db.Computers.Values)
                .SelectMany(each => each.Find("notes")?.Items ?? []).Where(note => note.File == value.File).Select(note => (string)note.Scalar!);
            var note = Assert.Single(notes.Distinct(), note => note.StartsWith(start, StringComparison.Ordinal));
            Assert.Contains(SourceNames, name => note.Contains(name, StringComparison.Ordinal));
        }
    }
}
