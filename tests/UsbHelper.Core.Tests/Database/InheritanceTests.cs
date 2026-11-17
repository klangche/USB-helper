using UsbHelper.Core.Database;

namespace UsbHelper.Core.Tests.Database;

public class InheritanceTests
{
    [Fact]
    public void OmittedFieldInheritsFromPlatformAndBase()
    {
        using var db = new TestDatabase().WriteComputer("computers/test/c.yaml", "test.c");

        var computer = db.Load().Computers[TestDatabase.IdOf("test.c")];

        Assert.Equal("x64", computer.Find("architecture")!.Scalar);
        Assert.Equal(TestDatabase.PlatformFile, computer.Find("architecture")!.File);
        Assert.Equal(7L, computer.Find("usb.max_tiers")!.Scalar);
        Assert.Equal("base.yaml", computer.Find("usb.max_tiers")!.File);
        Assert.Equal("unverified", computer.Find("verification.status")!.Scalar);
        Assert.Equal("base.yaml", computer.Find("verification.status")!.File);
    }

    [Fact]
    public void ExplicitValueOverridesInheritedValue()
    {
        using var db = new TestDatabase().WriteComputer("computers/c.yaml", "test.c", """
            built_in_hub_levels: 2
            """);

        var levels = db.Load().Computers[TestDatabase.IdOf("test.c")].Find("built_in_hub_levels")!;

        Assert.Equal(2L, levels.Scalar);
        Assert.Equal("computers/c.yaml", levels.File);
        Assert.Equal(5, levels.Line);
    }

    [Fact]
    public void ExplicitNullOverridesInheritedValueAndMeansUnknown()
    {
        using var db = new TestDatabase()
            .Write(TestDatabase.PlatformFile, TestDatabase.Platform + "\ndisplays:\n  max_external: 2\n")
            .WriteComputer("computers/c.yaml", "test.c", """
                displays:
                  max_external: null
                """);

        var maxExternal = db.Load().Computers[TestDatabase.IdOf("test.c")].Find("displays.max_external")!;

        Assert.Equal(ValueKind.Null, maxExternal.Kind);
        Assert.Equal("computers/c.yaml", maxExternal.File);
    }

    // #144: displays.mst loads and inherits like the other display fields.
    [Fact]
    public void DisplaysMstLoadsAndIsInherited()
    {
        using var db = new TestDatabase()
            .Write(TestDatabase.PlatformFile, TestDatabase.Platform + "\ndisplays:\n  mst: false\n")
            .WriteComputer("computers/c.yaml", "test.c");

        var mst = db.Load().Computers[TestDatabase.IdOf("test.c")].Find("displays.mst")!;

        Assert.Equal(ValueKind.Boolean, mst.Kind);
        Assert.Equal(false, mst.Scalar);
        Assert.Equal(TestDatabase.PlatformFile, mst.File);
    }

    [Fact]
    public void FieldDefinedNowhereIsMissing()
    {
        using var db = new TestDatabase().WriteComputer("computers/c.yaml", "test.c");

        Assert.Null(db.Load().Computers[TestDatabase.IdOf("test.c")].Find("displays.max_external"));
    }

    [Fact]
    public void EmptyListAddsNothing()
    {
        using var db = new TestDatabase().WriteComputer("computers/c.yaml", "test.c", """
            notes: []
            """);

        var notes = db.Load().Computers[TestDatabase.IdOf("test.c")].Find("notes")!;

        Assert.Equal(["Platform note."], notes.Items.Select(item => item.Scalar));
    }

    [Fact]
    public void ListAppendsChildItemsToInheritedItems()
    {
        using var db = new TestDatabase().WriteComputer("computers/c.yaml", "test.c", """
            notes:
              - "Computer note."
            """);

        var notes = db.Load().Computers[TestDatabase.IdOf("test.c")].Find("notes")!;

        Assert.Equal(["Platform note.", "Computer note."], notes.Items.Select(item => item.Scalar));
        Assert.Equal([TestDatabase.PlatformFile, "computers/c.yaml"], notes.Items.Select(item => item.File));
    }

    [Fact]
    public void EmptyMappingAddsNothing()
    {
        using var db = new TestDatabase()
            .Write(TestDatabase.PlatformFile, TestDatabase.Platform + "\ndisplays:\n  max_external: 2\n")
            .WriteComputer("computers/c.yaml", "test.c", """
                displays: {}
                """);

        Assert.Equal(2L, db.Load().Computers[TestDatabase.IdOf("test.c")].Find("displays.max_external")!.Scalar);
    }

    [Fact]
    public void MappingsMergeFieldByField()
    {
        using var db = new TestDatabase().WriteComputer("computers/c.yaml", "test.c", """
            verification:
              last_checked: 2026-10-29
            """);

        var computer = db.Load().Computers[TestDatabase.IdOf("test.c")];

        Assert.Equal("unverified", computer.Find("verification.status")!.Scalar);
        Assert.Equal("2026-10-29", computer.Find("verification.last_checked")!.Scalar);
    }

    [Fact]
    public void ProfileListsItsFilesBaseFirst()
    {
        using var db = new TestDatabase().WriteComputer("computers/c.yaml", "test.c");

        var loaded = db.Load();

        Assert.Equal(["base.yaml", TestDatabase.PlatformFile], loaded.Platforms["test-platform"].Files);
        Assert.Equal(["base.yaml", TestDatabase.PlatformFile, "computers/c.yaml"], loaded.Computers[TestDatabase.IdOf("test.c")].Files);
    }

    [Fact]
    public void PlatformAndChipNamesAreNotPartOfResolvedFields()
    {
        using var db = new TestDatabase().WriteComputer("computers/c.yaml", "test.c");

        var loaded = db.Load();
        var computer = loaded.Computers[TestDatabase.IdOf("test.c")];

        Assert.Null(computer.Find("platform"));
        Assert.Null(computer.Find("chip_family"));
        Assert.Null(computer.Find("chips"));
        Assert.Null(loaded.Platforms["test-platform"].Find("name"));
        Assert.Null(computer.Find("name"));
    }

    // Step 2 (#286) lists the operating systems with their chips; a computer names both.
    [Fact]
    public void ProfilesKnowTheirPlatformAndChip()
    {
        using var db = new TestDatabase().WriteComputer("computers/c.yaml", "test.c");

        var loaded = db.Load();

        foreach (var profile in new[] { loaded.Platforms["test-platform"], loaded.Computers[TestDatabase.IdOf("test.c")] })
        {
            Assert.Equal("test", profile.Platform);
            Assert.Equal("Test OS", profile.PlatformName);
            Assert.Equal("platform", profile.Chip);
            Assert.Equal("Test chip", profile.ChipName);
        }
    }

    [Fact]
    public void EveryChipIsAPlatformProfile()
    {
        using var db = new TestDatabase().Write("platforms/two.yaml", """
            name: Two
            chips:
              a: { name: A, built_in_hub_levels: 0 }
              b: { name: B, built_in_hub_levels: 1 }
            """);

        var platforms = db.Load().Platforms;

        Assert.Equal(["test-platform", "two-a", "two-b"], platforms.Keys.Order());
        Assert.Equal(1L, platforms["two-b"].Find("built_in_hub_levels")!.Scalar);
        Assert.Equal(["base.yaml", "platforms/two.yaml"], platforms["two-b"].Files);
    }

    [Fact]
    public void FilesStartingWithDotAreNotLoaded()
    {
        using var db = new TestDatabase().WriteComputer("computers/.template.yaml", "test.template");

        Assert.Empty(db.Load().Computers);
    }
}
