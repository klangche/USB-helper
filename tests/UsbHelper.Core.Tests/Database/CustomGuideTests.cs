using UsbHelper.Core.Database;

namespace UsbHelper.Core.Tests.Database;

/// <summary>The user's own guide, custom.yaml (#300): a blank to start from, validated as every guide.</summary>
public class CustomGuideTests
{
    private static readonly HardwareDatabase Db = HardwareDatabase.Load(RealDatabaseTests.DatabaseFolder());

    [Fact]
    public void TheBlankLoadsWithEveryPlatformAndChip()
    {
        var blank = CustomGuide.Blank(Db, "someone@example.com");

        var guide = CustomGuide.Load(Db, blank);

        Assert.Equal("custom", guide.Id);
        Assert.Equal("Custom", guide.Name);
        Assert.Equal("custom.yaml", guide.File);
        Assert.True(guide.Selectable);
        Assert.False(guide.ProAv);
        Assert.Same(Db.DefaultGuide, guide.Fallback);
        Assert.Equal("Made by someone@example.com", Assert.Single(guide.Find("notes")!.Items).Scalar);
        foreach (var profile in Db.Platforms.Values)
        {
            Assert.NotNull(guide.Find($"platforms.{profile.Platform}.chips.{profile.Chip}.limits.tiers.max"));
        }
        Assert.Contains("Leave a value empty  → fall back to the value above it", blank, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBlankWithoutAnEmailHasNoNote()
    {
        var guide = CustomGuide.Load(Db, CustomGuide.Blank(Db));

        Assert.Null(guide.Find("notes"));
    }

    [Fact]
    public void EmptyValuesFallBackToUsbHelper()
    {
        var guide = CustomGuide.Load(Db, CustomGuide.Blank(Db));

        foreach (var path in new[] { "tiers.max", "hubs.recommended", "usb2.hubs.max", "camera_audio.tiers.max" })
        {
            var value = guide.Limit("windows", "intel", path);
            Assert.Same(Db.DefaultGuide, value!.Guide);
            Assert.Equal(Db.DefaultGuide.Limit("windows", "intel", path)!.Value.Scalar, value.Value.Scalar);
        }
        Assert.Same(Db.DefaultGuide, guide.Mst("macos", "apple-silicon")!.Guide);
    }

    [Fact]
    public void AFilledInValueIsUsed()
    {
        var yaml = CustomGuide.Blank(Db).Replace("  tiers:\n    recommended:\n    max:\n", "  tiers:\n    recommended: 3\n    max: 4\n", StringComparison.Ordinal);

        var guide = CustomGuide.Load(Db, yaml);

        Assert.Equal(4L, guide.Limit("windows", "intel", "tiers.max")!.Number);
        Assert.Same(guide, guide.Limit("windows", "intel", "tiers.max")!.Guide);
    }

    [Fact]
    public void ProblemsAreListedWithTheirLinesInCustomYaml()
    {
        var yaml = CustomGuide.Blank(Db).Replace("name: Custom", "name: Mine", StringComparison.Ordinal)
            .Replace("  tiers:\n    recommended:\n", "  tiers:\n    recommended: lots\n", StringComparison.Ordinal)
            .Replace("  android:\n", "  amiga:\n", StringComparison.Ordinal);
        var lines = yaml.Split('\n');
        int LineOf(string text) => Array.FindIndex(lines, line => line.StartsWith(text, StringComparison.Ordinal)) + 1;

        var errors = Assert.Throws<DatabaseException>(() => CustomGuide.Load(Db, yaml)).Errors;

        Assert.All(errors, error => Assert.Equal("custom.yaml", error.File));
        Assert.Contains(errors, error => error.Line == LineOf("name: Mine") && error.Message.Contains("must be Custom", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Line == LineOf("    recommended: lots") && error.Message.Contains("limits.tiers.recommended", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Line == LineOf("  amiga:") && error.Message.Contains("platforms.amiga", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Message.Contains("Missing key 'platforms.android'", StringComparison.Ordinal));
    }

    [Fact]
    public void BrokenYamlIsReported()
    {
        var error = Assert.Single(Assert.Throws<DatabaseException>(() => CustomGuide.Load(Db, "name: Custom\nlimits: [\n")).Errors);

        Assert.Equal("custom.yaml", error.File);
    }

    [Fact]
    public void ACustomGuideMustBeSelectable()
    {
        var yaml = CustomGuide.Blank(Db).Replace("selectable: true", "selectable: false", StringComparison.Ordinal);

        var error = Assert.Single(Assert.Throws<DatabaseException>(() => CustomGuide.Load(Db, yaml)).Errors);
        Assert.Contains("'selectable' must be true", error.Message, StringComparison.Ordinal);
    }
}
