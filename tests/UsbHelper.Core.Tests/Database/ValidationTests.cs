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
        using var db = new TestDatabase().WriteComputer("computers/c.yaml", "test.c").WriteGuide(new() { ["tiers.recommended"] = "4" });

        var loaded = db.Load();

        Assert.Equal(["test-platform"], loaded.Platforms.Keys);
        Assert.Equal([TestDatabase.IdOf("test.c")], loaded.Computers.Keys);
        Assert.Equal(["guide", "usb-helper"], loaded.Guides.Keys.Order());
        Assert.Same(loaded.DefaultGuide, loaded.Guides["guide"].Fallback);
    }

    [Fact]
    public void UnknownFieldIsRejected()
    {
        using var db = new TestDatabase().WriteComputer("computers/c.yaml", "test.c", """
            limits_typo: 3
            """);

        Single(db.LoadErrors(), "computers/c.yaml", 5, "Unknown field 'limits_typo'");
    }

    [Fact]
    public void UnknownNestedFieldIsRejected()
    {
        using var db = new TestDatabase().WriteComputer("computers/c.yaml", "test.c", """
            ports:
              left:
                hdmi_typo: true
            """);

        Single(db.LoadErrors(), "computers/c.yaml", 7, "Unknown field 'ports.left.hdmi_typo'");
    }

    [Theory]
    [InlineData("displays:\n  max_external: five", 6, "expected a whole number")]
    [InlineData("displays:\n  max_external: -1", 6, "must not be negative")]
    [InlineData("displays:\n  max_external: \"5\"", 6, "expected a whole number")]
    [InlineData("built_in_hub_levels: -1", 5, "must not be negative")]
    [InlineData("built_in_hub_levels: one", 5, "expected a whole number")]
    [InlineData("ports:\n  left:\n    displayport_alt_mode: yes", 7, "expected true, false or null")]
    [InlineData("ports:\n  left:\n    thunderbolt: four", 7, "expected a whole number")]
    [InlineData("ports:\n  left:\n    hdmi: 1", 7, "expected true, false or null")]
    [InlineData("ports:\n  left:\n    limitations: none", 7, "expected a list")]
    [InlineData("displays:\n  restrictions: none", 6, "expected a list")]
    [InlineData("exceptions: just text", 5, "expected a list")]
    [InlineData("exceptions:\n  - 5", 5, "expected a list of text items")]
    [InlineData("verification:\n  status: confirmed", 6, "expected one of verified, partial, unverified")]
    [InlineData("verification:\n  status: null", 6, "expected one of verified, partial, unverified")]
    [InlineData("verification:\n  last_checked: 29.10.2026", 6, "expected a date as YYYY-MM-DD")]
    [InlineData("displays: 2", 5, "expected a mapping")]
    public void WrongTypeIsRejected(string yaml, int line, string messagePart)
    {
        using var db = new TestDatabase().WriteComputer("computers/c.yaml", "test.c", yaml);

        Single(db.LoadErrors(), "computers/c.yaml", line, messagePart);
    }

    // #284: a computer file never holds guide limits; they belong in guides/.
    [Fact]
    public void ComputerMustNotHoldLimits()
    {
        using var db = new TestDatabase().WriteComputer("computers/c.yaml", "test.c", "limits:\n  max_tiers: 3");

        Single(db.LoadErrors(), "computers/c.yaml", 5, "Unknown field 'limits'");
    }

    [Fact]
    public void PlatformMustNotHoldLimits()
    {
        using var db = new TestDatabase().Write(TestDatabase.PlatformFile, TestDatabase.Platform + "\nlimits:\n  max_tiers: 3\n");

        Single(db.LoadErrors(), TestDatabase.PlatformFile, 9, "Unknown field 'limits'");
    }

    [Theory]
    [InlineData("notes.yaml")]
    [InlineData("platforms/more/p.yaml")]
    [InlineData("guides/a/g.yaml")]
    [InlineData("guides/a/b/g.yaml")]
    [InlineData("guides/g.yml")]
    [InlineData("computers/c.yml")]
    [InlineData("base.yml")]
    public void FileThatIsNotReadIsRejected(string file)
    {
        using var db = new TestDatabase().Write(file, "name: p\n");

        Single(db.LoadErrors(), file, 0, "This file is not read");
    }

    [Fact]
    public void MissingPlatformIsRejected()
    {
        using var db = new TestDatabase().Write("computers/c.yaml", """
            platform: no-such-platform
            chip_family: platform
            manufacturer: Test
            model: Test model
            """);

        Single(db.LoadErrors(), "computers/c.yaml", 1, "Platform 'no-such-platform' does not exist");
    }

    [Fact]
    public void MissingChipIsRejected()
    {
        using var db = new TestDatabase().Write("computers/c.yaml", """
            platform: test
            chip_family: no-such-chip
            manufacturer: Test
            model: Test model
            """);

        Single(db.LoadErrors(), "computers/c.yaml", 2, "Platform 'test' has no chip 'no-such-chip'");
    }

    [Theory]
    [InlineData("platform")]
    [InlineData("chip_family")]
    public void ComputerMustNameItsPlatformAndChip(string field)
    {
        var yaml = "platform: test\nchip_family: platform\nmanufacturer: Test\nmodel: M\n";
        using var db = new TestDatabase().Write("computers/c.yaml", string.Join('\n', yaml.Split('\n').Where(line => !line.StartsWith(field + ":"))));

        Single(db.LoadErrors(), "computers/c.yaml", 1, $"Field '{field}' is required");
    }

    [Fact]
    public void SameComputerTwiceIsRejected()
    {
        using var db = new TestDatabase()
            .WriteComputer("computers/a.yaml", "Same model")
            .WriteComputer("computers/b.yaml", " same  MODEL");

        Single(db.LoadErrors(), "computers/b.yaml", 4,
            $"Duplicate id '{TestDatabase.IdOf("Same model")}' (computed from manufacturer and model), already used in computers/a.yaml");
    }

    [Fact]
    public void SamePlatformProfileTwiceIsRejected()
    {
        // "a-b" with chip "c" and "a" with chip "b-c" are both "a-b-c".
        using var db = new TestDatabase()
            .Write("platforms/a-b.yaml", "name: A\nchips:\n  c: { name: C, built_in_hub_levels: 0 }\n")
            .Write("platforms/a.yaml", "name: A\nchips:\n  b-c: { name: C, built_in_hub_levels: 0 }\n");

        Single(db.LoadErrors(), "platforms/a.yaml", 3, "Duplicate id 'a-b-c', already used in platforms/a-b.yaml");
    }

    [Fact]
    public void WrittenComputerIdIsRejected()
    {
        using var db = new TestDatabase().Write("computers/c.yaml", """
            id: my-own-id
            platform: test
            chip_family: platform
            manufacturer: Test
            model: Test model
            """);

        Single(db.LoadErrors(), "computers/c.yaml", 1, "Computer profiles have no 'id': it is computed from manufacturer and model.");
    }

    [Fact]
    public void ComputerWithoutModelTextIsRejected()
    {
        using var db = new TestDatabase().WriteComputer("computers/c.yaml", "null");

        Single(db.LoadErrors(), "computers/c.yaml", 4, "'manufacturer' and 'model' must be text: the id is computed from them.");
    }

    [Fact]
    public void DuplicateFieldInOneFileIsRejected()
    {
        using var db = new TestDatabase().WriteComputer("computers/c.yaml", "test.c", """
            model: Second model
            """);

        Single(db.LoadErrors(), "computers/c.yaml", 5, "Duplicate key model");
    }

    [Fact]
    public void InvalidYamlSyntaxIsRejectedWithLine()
    {
        using var db = new TestDatabase().Write("platforms/broken.yaml", "name: Broken\nchips: {}\nnotes: [unclosed\n");

        var error = Assert.Single(db.LoadErrors());
        Assert.Equal("platforms/broken.yaml", error.File);
        Assert.True(error.Line >= 3);
    }

    [Fact]
    public void PlatformMustHaveName()
    {
        using var db = new TestDatabase().Write("platforms/p.yaml", "chips:\n  c: { name: C, built_in_hub_levels: 0 }\n");

        Single(db.LoadErrors(), "platforms/p.yaml", 1, "Field 'name' is required");
    }

    [Fact]
    public void PlatformMustHaveChips()
    {
        using var db = new TestDatabase().Write("platforms/p.yaml", "name: P\n");

        Single(db.LoadErrors(), "platforms/p.yaml", 1, "Field 'chips' is required");
    }

    [Fact]
    public void ChipMustHaveName()
    {
        using var db = new TestDatabase().Write("platforms/p.yaml", "name: P\nchips:\n  c:\n    built_in_hub_levels: 0\n");

        Single(db.LoadErrors(), "platforms/p.yaml", 3, "Field 'chips.c.name' is required");
    }

    // Unknown stays unknown: a chip says null rather than leaving its hub levels out.
    [Fact]
    public void ChipMustSetBuiltInHubLevels()
    {
        using var db = new TestDatabase().Write("platforms/p.yaml", "name: P\nchips:\n  c:\n    name: C\n");

        Single(db.LoadErrors(), "platforms/p.yaml", 3, "Chip 'c' must set 'built_in_hub_levels'");
    }

    [Fact]
    public void UnknownChipFieldIsRejected()
    {
        using var db = new TestDatabase().Write("platforms/p.yaml", "name: P\nchips:\n  c:\n    name: C\n    built_in_hub_levels: 0\n    max_tiers: 5\n");

        Single(db.LoadErrors(), "platforms/p.yaml", 6, "Unknown field 'chips.c.max_tiers'");
    }

    [Fact]
    public void PlatformMustNotDefinePorts()
    {
        using var db = new TestDatabase().Write(TestDatabase.PlatformFile, TestDatabase.Platform + "\nports:\n  left:\n    usb: usb-3\n");

        Single(db.LoadErrors(), TestDatabase.PlatformFile, 9, "Unknown field 'ports'");
    }

    [Fact]
    public void BaseMustNotDefinePorts()
    {
        using var db = new TestDatabase().Write("base.yaml", TestDatabase.Base + "\nports:\n  left:\n    usb: usb-3\n");

        Single(db.LoadErrors(), "base.yaml", 7, "Unknown field 'ports'");
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
        using var db = new TestDatabase().Write("base.yaml", TestDatabase.Base.Replace("schema_version: 2", "schema_version: 1"));

        Single(db.LoadErrors(), "base.yaml", 1, "'schema_version' must be 2");
    }

    [Fact]
    public void BaseMustSetTheUsbMaximum()
    {
        using var db = new TestDatabase().Write("base.yaml", "schema_version: 2\n");

        Single(db.LoadErrors(), "base.yaml", 1, "Field 'usb.max_tiers' is required");
    }

    [Fact]
    public void UnknownBaseFieldIsRejected()
    {
        using var db = new TestDatabase().Write("base.yaml", TestDatabase.Base + "\nsurprise: 1\n");

        Single(db.LoadErrors(), "base.yaml", 7, "Unknown field 'surprise'");
    }

    // MST moved to the guides (#285): a platform file holds facts only.
    [Fact]
    public void PlatformMustNotHoldMst()
    {
        using var db = new TestDatabase().Write(TestDatabase.PlatformFile, TestDatabase.Platform + "\ndisplays:\n  mst: true\n");

        Single(db.LoadErrors(), TestDatabase.PlatformFile, 9, "Unknown field 'displays'");
    }

    [Fact]
    public void UsbHelpersGuideIsRequired()
    {
        using var db = new TestDatabase { WithDefaultGuide = false };

        Single(db.LoadErrors(), "guides/usb-helper.yaml", 0, "File not found");
    }

    // USB-Helper's guide is the fallback, so its global limits and every mst are filled in (owner decision 2026-11-17).
    [Theory]
    [InlineData("tiers.max", "Key 'limits.tiers.max' must be filled in")]
    [InlineData("usb4.hubs.max", "Key 'limits.buses.usb4.hubs.max' must be filled in")]
    [InlineData("camera_audio.tiers.max", "Key 'limits.camera_audio.tiers.max' must be filled in")]
    [InlineData("platforms.test.chips.platform.limits.mst", "Key 'platforms.test.chips.platform.mst' must be filled in")]
    public void UsbHelpersGuideMustFillEveryValue(string key, string message)
    {
        using var db = new TestDatabase();
        db.UsbHelper[key] = "";

        var errors = db.LoadErrors();

        Assert.Contains(errors, error => error.File == "guides/usb-helper.yaml" && error.Message.Contains(message, StringComparison.Ordinal) && error.Line > 0);
    }

    // A chip or platform only states what differs; the rest comes from the level above.
    [Fact]
    public void UsbHelpersChipsMayLeaveLimitsEmpty()
    {
        using var db = new TestDatabase();
        db.UsbHelper["platforms.test.chips.platform.limits.tiers.max"] = "";

        Assert.Single(db.Load().Guides);
    }

    // An empty value in another guide falls back to USB-Helper's: allowed everywhere.
    [Fact]
    public void OtherGuidesMayLeaveEveryValueEmpty()
    {
        using var db = new TestDatabase().WriteGuide([], id: "later", name: "Later", selectable: false);

        var guide = db.Load().Guides["later"];

        Assert.Equal("Later", guide.Name);
        Assert.False(guide.Selectable);
        Assert.Equal("guides/later.yaml", guide.File);
    }

    [Theory]
    [InlineData("hubs.max", "-1", "expected a whole number, none, or nothing")]
    [InlineData("usb3.hubs.max", "five", "expected a whole number, none, or nothing")]
    [InlineData("tiers.max", "8", "Key 'limits.tiers.max' (8) must not be above usb.max_tiers (7) in base.yaml")]
    [InlineData("camera_audio.tiers.recommended", "8", "must not be above usb.max_tiers")]
    [InlineData("platforms.test.chips.platform.limits.tiers.max", "8", "Key 'platforms.test.chips.platform.limits.tiers.max' (8) must not be above")]
    [InlineData("platforms.test.chips.platform.limits.mst", "2", "Key 'platforms.test.chips.platform.mst': expected 1 (yes) or 0 (no)")]
    [InlineData("platforms.test.chips.platform.limits.mst", "true", "expected 1 (yes) or 0 (no)")]
    [InlineData("exceeded_note", "5", "expected text")]
    public void WrongGuideValueIsRejected(string key, string value, string messagePart)
    {
        using var db = new TestDatabase().WriteGuide(new() { [key] = value });

        var error = Assert.Single(db.LoadErrors());
        Assert.Equal("guides/guide.yaml", error.File);
        Assert.Contains(messagePart, error.Message);
    }

    // The owner's "max x letters": a named constant, 160.
    [Fact]
    public void ExceededNoteLongerThanTheLimitIsRejected()
    {
        using var db = new TestDatabase().WriteGuide(new() { ["platforms.test.chips.platform.limits.exceeded_note"] = new string('x', Guide.ExceededNoteMaxLength + 1) });

        var error = Assert.Single(db.LoadErrors());
        Assert.Equal("guides/guide.yaml", error.File);
        Assert.Contains($"has {Guide.ExceededNoteMaxLength + 1} characters; at most {Guide.ExceededNoteMaxLength}", error.Message);
        Assert.True(error.Line > 0);
    }

    [Fact]
    public void ExceededNoteOfTheLimitIsAccepted()
    {
        using var db = new TestDatabase().WriteGuide(new() { ["exceeded_note"] = new string('x', Guide.ExceededNoteMaxLength) });

        Assert.Single(db.Load().Guides, guide => guide.Key == "guide");
    }

    // A returned guide cannot quietly break anything: every guide has the platforms, chips and keys of USB-Helper's.
    private static TestDatabase WithCopy(Func<string, string> change)
    {
        var db = new TestDatabase();
        return db.Write("guides/copy.yaml", change(db.UsbHelperFile().Replace("name: USB-Helper", "name: Copy")));
    }

    [Fact]
    public void GuideWithAMissingPlatformIsRejected()
    {
        using var db = WithCopy(content => content[..content.IndexOf("platforms:", StringComparison.Ordinal)] + "platforms: {}\n");

        var error = Assert.Single(db.LoadErrors());
        Assert.Equal("guides/copy.yaml", error.File);
        Assert.Contains("Missing key 'platforms.test'", error.Message);
    }

    [Fact]
    public void GuideWithAMissingChipIsRejected()
    {
        using var db = WithCopy(content => content[..content.IndexOf("    chips:", StringComparison.Ordinal)] + "    chips: {}\n");

        var error = Assert.Single(db.LoadErrors());
        Assert.Equal("guides/copy.yaml", error.File);
        Assert.Contains("Missing key 'platforms.test.chips.platform'", error.Message);
    }

    [Fact]
    public void GuideWithAnExtraKeyIsRejected()
    {
        using var db = WithCopy(content => content.Replace("limits:\n", "limits:\n  usb5:\n    hubs:\n"));

        var error = Assert.Single(db.LoadErrors());
        Assert.Equal("guides/copy.yaml", error.File);
        Assert.Contains("Unknown key 'limits.usb5'", error.Message);
        Assert.True(error.Line > 0);
    }

    // #297: hops are no longer a limit, so a guide that still has them is rejected like any other unknown key.
    [Theory]
    [InlineData("limits:\n", "limits:\n  hops:\n    recommended: 2\n    max: 4\n", "Unknown key 'limits.hops'")]
    [InlineData("  camera_audio:\n", "  camera_audio:\n    hops:\n      recommended: 2\n      max: 4\n", "Unknown key 'limits.camera_audio.hops'")]
    public void GuideWithHopsIsRejected(string before, string after, string message)
    {
        using var db = WithCopy(content => content.Replace(before, after));

        var error = Assert.Single(db.LoadErrors());
        Assert.Equal("guides/copy.yaml", error.File);
        Assert.Contains(message, error.Message);
        Assert.True(error.Line > 0);
    }

    [Fact]
    public void GuideWithAnExtraPlatformIsRejected()
    {
        using var db = WithCopy(content => content + "  beos:\n    mst: 0\n    limits: {}\n    chips: {}\n");

        var error = Assert.Single(db.LoadErrors());
        Assert.Contains("Unknown key 'platforms.beos'", error.Message);
    }

    // #299: every guide says whether it is a ProAV guide.
    [Theory]
    [InlineData("proav: 1\n", "")]
    [InlineData("proav: 1\n", "proav: 2\n")]
    public void GuideMustSayWhetherItIsProAv(string written, string replaced)
    {
        using var db = WithCopy(content => content.Replace(written, replaced));

        var error = Assert.Single(db.LoadErrors());
        Assert.Contains("Field 'proav' is required", error.Message);
    }

    [Fact]
    public void GuideMustHaveNameAndSelectable()
    {
        using var db = WithCopy(content => content.Replace("name: Copy\nselectable: true\n", ""));

        var errors = db.LoadErrors();

        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, error => error.Message.Contains("Field 'name' is required"));
        Assert.Contains(errors, error => error.Message.Contains("Field 'selectable' is required"));
    }

    // A guide in a subfolder is the old shape: reported, never silently left out.
    [Fact]
    public void GuideInAFolderIsNotRead()
    {
        using var db = new TestDatabase().Write("guides/old/all.yaml", "{}\n");

        Single(db.LoadErrors(), "guides/old/all.yaml", 0, "This file is not read");
    }

    [Fact]
    public void AllErrorsAreReportedTogether()
    {
        using var db = new TestDatabase()
            .WriteComputer("computers/a.yaml", "test.a", "colour: red")
            .WriteComputer("computers/b.yaml", "test.b", "verification:\n  status: maybe");

        var exception = Assert.Throws<DatabaseException>(db.Load);

        Assert.Equal(2, exception.Errors.Count);
        Assert.Contains("computers/a.yaml:5: Unknown field 'colour'.", exception.Message);
        Assert.Contains("computers/b.yaml:6:", exception.Message);
    }
}
