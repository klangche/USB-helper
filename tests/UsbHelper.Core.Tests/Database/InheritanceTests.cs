using UsbHelper.Core.Database;

namespace UsbHelper.Core.Tests.Database;

public class InheritanceTests
{
    [Fact]
    public void OmittedFieldInheritsFromPlatformAndBase()
    {
        using var db = new TestDatabase().WriteComputer("computers/test/c.yaml", "test.c");

        var computer = db.Load().Computers["test.c"];

        Assert.Equal("x64", computer.Find("architecture")!.Scalar);
        Assert.Equal("platforms/test-platform.yaml", computer.Find("architecture")!.File);
        Assert.Equal("unverified", computer.Find("verification.status")!.Scalar);
        Assert.Equal("base.yaml", computer.Find("verification.status")!.File);
    }

    [Fact]
    public void ExplicitValueOverridesInheritedValue()
    {
        using var db = new TestDatabase().WriteComputer("computers/c.yaml", "test.c", """
            architecture: arm64
            """);

        var architecture = db.Load().Computers["test.c"].Find("architecture")!;

        Assert.Equal("arm64", architecture.Scalar);
        Assert.Equal("computers/c.yaml", architecture.File);
        Assert.Equal(9, architecture.Line);
    }

    [Fact]
    public void ExplicitNullOverridesInheritedValueAndMeansUnknown()
    {
        using var db = new TestDatabase()
            .Write("platforms/test-platform.yaml", TestDatabase.Platform + "\ndisplays:\n  max_external: 2\n")
            .WriteComputer("computers/c.yaml", "test.c", """
                displays:
                  max_external: null
                """);

        var maxExternal = db.Load().Computers["test.c"].Find("displays.max_external")!;

        Assert.Equal(ValueKind.Null, maxExternal.Kind);
        Assert.Equal("computers/c.yaml", maxExternal.File);
    }

    [Fact]
    public void FieldDefinedNowhereIsMissing()
    {
        using var db = new TestDatabase().WriteComputer("computers/c.yaml", "test.c");

        Assert.Null(db.Load().Computers["test.c"].Find("displays.max_external"));
    }

    [Fact]
    public void EmptyListAddsNothing()
    {
        using var db = new TestDatabase().WriteComputer("computers/c.yaml", "test.c", """
            notes: []
            """);

        var notes = db.Load().Computers["test.c"].Find("notes")!;

        Assert.Equal(["Platform note."], notes.Items.Select(item => item.Scalar));
    }

    [Fact]
    public void ListAppendsChildItemsToInheritedItems()
    {
        using var db = new TestDatabase().WriteComputer("computers/c.yaml", "test.c", """
            notes:
              - "Computer note."
            """);

        var notes = db.Load().Computers["test.c"].Find("notes")!;

        Assert.Equal(["Platform note.", "Computer note."], notes.Items.Select(item => item.Scalar));
        Assert.Equal(["platforms/test-platform.yaml", "computers/c.yaml"], notes.Items.Select(item => item.File));
    }

    [Fact]
    public void EmptyMappingAddsNothing()
    {
        using var db = new TestDatabase()
            .Write("platforms/test-platform.yaml", TestDatabase.Platform + "\ndisplays:\n  max_external: 2\n")
            .WriteComputer("computers/c.yaml", "test.c", """
                displays: {}
                """);

        Assert.Equal(2L, db.Load().Computers["test.c"].Find("displays.max_external")!.Scalar);
    }

    [Fact]
    public void MappingsMergeFieldByField()
    {
        using var db = new TestDatabase().WriteComputer("computers/c.yaml", "test.c", """
            verification:
              last_checked: 2026-10-29
            """);

        var computer = db.Load().Computers["test.c"];

        Assert.Equal("unverified", computer.Find("verification.status")!.Scalar);
        Assert.Equal("2026-10-29", computer.Find("verification.last_checked")!.Scalar);
    }

    [Fact]
    public void ProfileListsItsFilesBaseFirst()
    {
        using var db = new TestDatabase().WriteComputer("computers/c.yaml", "test.c");

        var loaded = db.Load();

        Assert.Equal(["base.yaml", "platforms/test-platform.yaml"], loaded.Platforms["test-platform"].Files);
        Assert.Equal(["base.yaml", "platforms/test-platform.yaml", "computers/c.yaml"], loaded.Computers["test.c"].Files);
    }

    [Fact]
    public void IdAndInheritsAreNotPartOfResolvedFields()
    {
        using var db = new TestDatabase().WriteComputer("computers/c.yaml", "test.c");

        var computer = db.Load().Computers["test.c"];

        Assert.Null(computer.Find("id"));
        Assert.Null(computer.Find("inherits"));
    }

    [Fact]
    public void FilesStartingWithDotAreNotLoaded()
    {
        using var db = new TestDatabase().WriteComputer("computers/.template.yaml", "test.template");

        Assert.Empty(db.Load().Computers);
    }
}
