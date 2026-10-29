using UsbHelper.Core.Database;

namespace UsbHelper.Core.Tests.Database;

public class ValidationTests
{
    private static DatabaseError Single(IReadOnlyList<DatabaseError> errors, string file, int line, string messagePart)
    {
        var error = Assert.Single(errors);
        Assert.Equal(file, error.File);
        Assert.Equal(line, error.Line);
        Assert.Contains(messagePart, error.Message);
        return error;
    }

    [Fact]
    public void ValidDatabaseLoads()
    {
        using var db = new TestDatabase().WriteComputer("computers/c.yaml", "test.c");

        var loaded = db.Load();

        Assert.Equal(["test-platform"], loaded.Platforms.Keys);
        Assert.Equal(["test.c"], loaded.Computers.Keys);
    }

    [Fact]
    public void UnknownFieldIsRejected()
    {
        using var db = new TestDatabase().WriteComputer("computers/c.yaml", "test.c", """
            limits_typo: 3
            """);

        Single(db.LoadErrors(), "computers/c.yaml", 9, "Unknown field 'limits_typo'");
    }

    [Fact]
    public void UnknownNestedFieldIsRejected()
    {
        using var db = new TestDatabase().WriteComputer("computers/c.yaml", "test.c", """
            ports:
              left:
                hdmi_typo: true
            """);

        Single(db.LoadErrors(), "computers/c.yaml", 11, "Unknown field 'ports.left.hdmi_typo'");
    }

    [Theory]
    [InlineData("displays:\n  max_external: five", 10, "expected a whole number")]
    [InlineData("displays:\n  max_external: -1", 10, "must not be negative")]
    [InlineData("displays:\n  max_external: \"5\"", 10, "expected a whole number")]
    [InlineData("ports:\n  left:\n    displayport_alt_mode: yes", 11, "expected true, false or null")]
    [InlineData("ports:\n  left:\n    thunderbolt: four", 11, "expected a whole number")]
    [InlineData("ports:\n  left:\n    hdmi: 1", 11, "expected true, false or null")]
    [InlineData("ports:\n  left:\n    limitations: none", 11, "expected a list")]
    [InlineData("displays:\n  restrictions: none", 10, "expected a list")]
    [InlineData("exceptions: just text", 9, "expected a list")]
    [InlineData("exceptions:\n  - 5", 9, "expected a list of text items")]
    [InlineData("verification:\n  status: confirmed", 10, "expected one of verified, partial, unverified")]
    [InlineData("verification:\n  status: null", 10, "expected one of verified, partial, unverified")]
    [InlineData("verification:\n  last_checked: 29.10.2026", 10, "expected a date as YYYY-MM-DD")]
    [InlineData("displays: 2", 9, "expected a mapping")]
    public void WrongTypeIsRejected(string yaml, int line, string messagePart)
    {
        using var db = new TestDatabase().WriteComputer("computers/c.yaml", "test.c", yaml);

        Single(db.LoadErrors(), "computers/c.yaml", line, messagePart);
    }

    [Fact]
    public void MissingParentIsRejected()
    {
        using var db = new TestDatabase().Write("computers/c.yaml", """
            id: test.c
            inherits: no-such-platform
            manufacturer: Test
            model: Test model
            limits:
              max_hubs: null
              max_tiers: null
              max_hops: null
            """);

        Single(db.LoadErrors(), "computers/c.yaml", 2, "Parent profile 'no-such-platform' does not exist");
    }

    [Fact]
    public void CircularInheritanceIsRejected()
    {
        // Computers may only inherit from platforms and platforms only from base, so a cycle cannot resolve.
        using var db = new TestDatabase()
            .Write("computers/a.yaml", "id: test.a\ninherits: test.b\nmanufacturer: T\nmodel: A\nlimits: {max_hubs: null, max_tiers: null, max_hops: null}\n")
            .Write("computers/b.yaml", "id: test.b\ninherits: test.a\nmanufacturer: T\nmodel: B\nlimits: {max_hubs: null, max_tiers: null, max_hops: null}\n")
            .Write("platforms/loop.yaml", "id: loop\ninherits: loop\n");

        var errors = db.LoadErrors();

        Assert.Equal(["computers/a.yaml", "computers/b.yaml", "platforms/loop.yaml"], errors.Select(error => error.File).Order());
        Assert.All(errors, error => Assert.Equal(2, error.Line));
        Assert.Contains(errors, error => error.Message.Contains("a computer profile must inherit from a platform profile"));
        Assert.Contains(errors, error => error.Message.Contains("must inherit from 'base'"));
    }

    [Fact]
    public void DuplicateIdIsRejected()
    {
        using var db = new TestDatabase()
            .WriteComputer("computers/a.yaml", "test.same")
            .WriteComputer("computers/b.yaml", "test.same");

        Single(db.LoadErrors(), "computers/b.yaml", 1, "Duplicate id 'test.same', already used in computers/a.yaml");
    }

    [Fact]
    public void DuplicateFieldInOneFileIsRejected()
    {
        using var db = new TestDatabase().WriteComputer("computers/c.yaml", "test.c", """
            model: Second model
            """);

        Single(db.LoadErrors(), "computers/c.yaml", 9, "Duplicate key model");
    }

    [Fact]
    public void InvalidYamlSyntaxIsRejectedWithLine()
    {
        using var db = new TestDatabase().Write("platforms/broken.yaml", "id: broken\ninherits: base\nnotes: [unclosed\n");

        var error = Assert.Single(db.LoadErrors());
        Assert.Equal("platforms/broken.yaml", error.File);
        Assert.True(error.Line >= 3);
    }

    [Fact]
    public void ComputerMustSetLimitsExplicitly()
    {
        using var db = new TestDatabase().Write("computers/c.yaml", """
            id: test.c
            inherits: test-platform
            manufacturer: Test
            model: Test model
            limits:
              max_hubs: 4
            """);

        var errors = db.LoadErrors();

        Assert.Equal(["limits.max_hops", "limits.max_tiers"], errors.Select(error => error.Message.Split('\'')[1]).Order());
    }

    [Fact]
    public void ComputerMustHaveIdAndInherits()
    {
        using var db = new TestDatabase().Write("computers/c.yaml", "manufacturer: Test\nmodel: M\nlimits: {max_hubs: null, max_tiers: null, max_hops: null}\n");

        var errors = db.LoadErrors();

        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, error => error.Message.Contains("'id' is required"));
        Assert.Contains(errors, error => error.Message.Contains("'inherits' is required"));
    }

    [Fact]
    public void PlatformMustNotDefinePorts()
    {
        using var db = new TestDatabase().Write("platforms/test-platform.yaml", TestDatabase.Platform + "\nports:\n  left:\n    usb: usb-3\n");

        Single(db.LoadErrors(), "platforms/test-platform.yaml", 7, "'ports' belongs in computer profiles");
    }

    [Fact]
    public void ComputerInheritingBaseIsRejected()
    {
        using var db = new TestDatabase().Write("computers/c.yaml", "id: test.c\ninherits: base\nmanufacturer: T\nmodel: M\nlimits: {max_hubs: null, max_tiers: null, max_hops: null}\n");

        Single(db.LoadErrors(), "computers/c.yaml", 2, "'base' is not a platform profile");
    }

    [Fact]
    public void BaseMustNotDefinePorts()
    {
        using var db = new TestDatabase().Write("base.yaml", TestDatabase.Base + "\n  ports:\n    left:\n      usb: usb-3\n");

        Single(db.LoadErrors(), "base.yaml", 9, "'ports' belongs in computer profiles, not base.yaml");
    }

    [Fact]
    public void MissingBaseIsRejected()
    {
        using var db = new TestDatabase();
        File.Delete(Path.Combine(db.Folder, "base.yaml"));

        Single(db.LoadErrors(), "base.yaml", 0, "File not found");
    }

    [Fact]
    public void WrongSchemaVersionIsRejected()
    {
        using var db = new TestDatabase().Write("base.yaml", TestDatabase.Base.Replace("schema_version: 1", "schema_version: 2"));

        Single(db.LoadErrors(), "base.yaml", 1, "'schema_version' must be 1");
    }

    [Fact]
    public void InvalidBaseDefaultsAreRejected()
    {
        using var db = new TestDatabase().Write("base.yaml", TestDatabase.Base + "\n  surprise: 1\n");

        Single(db.LoadErrors(), "base.yaml", 9, "Unknown field 'surprise'");
    }

    [Fact]
    public void AllErrorsAreReportedTogether()
    {
        using var db = new TestDatabase()
            .WriteComputer("computers/a.yaml", "test.a", "colour: red")
            .WriteComputer("computers/b.yaml", "test.b", "verification:\n  status: maybe");

        var exception = Assert.Throws<DatabaseException>(db.Load);

        Assert.Equal(2, exception.Errors.Count);
        Assert.Contains("computers/a.yaml:9: Unknown field 'colour'.", exception.Message);
        Assert.Contains("computers/b.yaml:10:", exception.Message);
    }
}
