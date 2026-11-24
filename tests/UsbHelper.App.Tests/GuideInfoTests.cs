using UsbHelper.Core.Database;

namespace UsbHelper.App.Tests;

/// <summary>A philosophy's details behind its ⓘ button (#397): a short hover and a panel with everything.</summary>
public class GuideInfoTests
{
    private static readonly HardwareDatabase Db = DatabaseFormats.Read(4, EmbeddedDatabase.Files()[4]);

    [Fact]
    public void TheHoverHasTheMenuLineTheSignOffAndTheSource()
    {
        var ochno = Db.Guides["ochno"];

        var summary = GuideInfo.Of(ochno, Db.Platforms)!.Summary.Split('\n');

        Assert.Equal(GuideLine.For(ochno, Db.Platforms), summary[0]);
        Assert.Equal(ochno.VersionHover, summary[1]);
        Assert.StartsWith("Source: Guide: USB hub chains in video conference room", summary[2], StringComparison.Ordinal);
    }

    // Every rule is a line, with its quote and source under it; what each measure counts is said in words.
    [Fact]
    public void ThePanelListsWhatItCountsAndEveryLimitWithItsQuote()
    {
        foreach (var guide in Db.Guides.Values.Where(guide => !guide.NeedsNewerApp))
        {
            var lines = GuideInfo.Of(guide, Db.Platforms)!.Lines.Select(line => line.Text).ToList();

            Assert.Contains("What it counts", lines);
            Assert.All(guide.Rules, rule => Assert.Contains(GuideLine.Part(rule, Db.Platforms), lines));
            Assert.All(guide.Rules.Where(rule => rule.Quote != null), rule => Assert.Contains(lines, line => line.Contains(rule.Quote!, StringComparison.Ordinal)));
            Assert.All(guide.CableNotes, note => Assert.Contains(note.Text, lines));
        }
        Assert.Contains("hub chain: the computer's own hubs + hubs + marked hubs", GuideInfo.Of(Db.Guides["ochno"])!.Lines.Select(line => line.Text));
    }

    [Fact]
    public void AV3GuideHasNoDetails() => Assert.Null(GuideInfo.Of(EmbeddedDatabaseTests.V3().Guides.Values.First()));
}
