using System.Text.Json;
using System.Text.RegularExpressions;
using UsbHelper.Core.Database;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;
using static UsbHelper.Core.Tests.Detection.TestTree;

namespace UsbHelper.Core.Tests.Database;

/// <summary>
/// The readable layout of a v4 philosophy (#386): a shorter way to write the same rules. Each readable file here loads to
/// exactly the measures and rules of its full form, and judges the same.
/// </summary>
public partial class ReadableLayoutTests
{
    private static readonly string Base = V4Tests.Base + """

        counts:
          chain:
            explain: hubs in a row in front of a device, the computer's own hubs included
            adds: computer_hub, hub, marked_hub
          tiers:
            explain: USB tiers, from the computer's root hub to the device itself
            adds: root_hub, computer_hub, hub, marked_hub, cable_length, device
          room:
            explain: only the hubs in the room, not the computer's own
            adds: hub, marked_hub
        """;

    // V4Tests.Room in the readable layout: per bus, per device kind, per platform/chip, and "none".
    private const string ReadableRoom = """
        name: Room  # the name in the menu
        selectable: true
        proav: 1
        basis:
          source: room
          quote: at most four hubs in the room
        sources:
          room:
            link: https://example.com/room
            title: Room guide
            version: "1.0"
        counts: room, chain
        computer_hubs:
          use: computer, chip
          if unknown: at least
        limits:
          room:
            max: 4
            source: room
            quote: at most four hubs in the room
          chain:
            max: 5
            source: room
            quote: >
              five hubs
              in a chain
            usb2:
              max: 2
              source: room
              quote: two on USB 2
            audio:
              max: 1
              source: room
              quote: audio on one hub
            cameras:
              max: 3
              source: room
              quote: cameras on three hubs
            on windows/intel:
              max: 0
              recommended: none
              source: room
              quote: "Intel: no hub"
        """;

    // The example in #386: Ochno, readable.
    private const string ReadableOchno = """
        # Ochno: hubs in a row in front of a device, the computer's own hubs included.
        name: Ochno
        selectable: true
        proav: 1
        basis:
          source: ochno
          quote: having max 5 usb hubs daisy-chained
        sources:
          ochno:
            title: Guide
        counts: chain            # chain = hubs in front of a device · tiers = USB tiers · room = only the room's hubs

        computer_hubs:           # the hubs inside the computer
          use: measured, computer, chip
          if unknown: at least

        limits:
          chain:
            max: 5
            source: ochno
            quote: having max 5 usb hubs daisy-chained
          room:
            max: 4
            source: ochno
            quote: >
              to ensure that the video conference room works with
              100% of all laptops, the max available hubs are 4
        notes:
          - >
            A note with "quotes"
            on two lines.
        """;

    private const string FullOchno = """
        name: Ochno
        selectable: true
        proav: 1
        basis: { source: ochno, quote: "having max 5 usb hubs daisy-chained" }
        sources:
          ochno: { title: Guide }
        measures:
          chain:
            counts: [computer_hub, hub, marked_hub]
            computer_hub: { from: [detected, computer, chip], unknown: open }
          room:
            counts: [hub, marked_hub]
        rules:
          - { measure: chain, max: 5, source: ochno, quote: "having max 5 usb hubs daisy-chained" }
          - { measure: room, max: 4, source: ochno, quote: "to ensure that the video conference room works with 100% of all laptops, the max available hubs are 4" }
        notes:
          - "A note with \"quotes\" on two lines."
        """;

    // Custom counting with a weight, and Crestron's computer hub count and active cables, in both forms.
    private const string ReadableCustom = """
        name: Custom
        selectable: true
        proav: 1
        own: true
        measures:
          weighted:
            counts:
              - hub
              - marked_hub
            weights:
              marked_hub: 2
        counts: tiers
        computer_hubs:
          use: measured, computer, chip
          if unknown: 1
        active cables:
          usb2: 5
          usb3: 3
        limits:
          tiers:
            max: 7
          weighted:
            recommended: 2
            max: 3
        """;

    private const string FullCustom = """
        name: Custom
        selectable: true
        proav: 1
        own: true
        measures:
          weighted:
            counts: [hub, marked_hub]
            weights: { marked_hub: 2 }
          tiers:
            counts: [root_hub, computer_hub, hub, marked_hub, cable_length, device]
            computer_hub: { from: [detected, computer, chip], unknown: 1 }
            cable_length: { usb2_metres_per_tier: 5, usb3_metres_per_tier: 3 }
        rules:
          - { measure: tiers, max: 7 }
          - { measure: weighted, recommended: 2, max: 3 }
        """;

    private static Dictionary<string, string> Files(params (string File, string Yaml)[] guides)
    {
        var files = new Dictionary<string, string>
        {
            ["base.yaml"] = Base,
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

    private static Guide Load(string yaml, string id = "guide") => HardwareDatabase.LoadV4(Files(($"guides/{id}.yaml", yaml))).Guides[id];

    private static DatabaseError Error(string yaml) =>
        Assert.Single(Assert.Throws<DatabaseException>(() => HardwareDatabase.LoadV4(Files(("guides/guide.yaml", yaml)))).Errors);

    // Everything a guide is read into, line numbers left out: they differ between the two forms.
    private static string Loaded(Guide guide, bool root = true) => LineNumbers().Replace(JsonSerializer.Serialize(new
    {
        Root = root ? Plain(guide.Root) : null,
        guide.MeasureDefinitions,
        guide.Rules,
        guide.SourceDefinitions,
        guide.CableNotes,
        guide.CableModels,
        guide.Unsupported,
    }), "");

    private static object? Plain(ProfileValue value) => value.Kind switch
    {
        ValueKind.Mapping => new SortedDictionary<string, object?>(value.Fields.ToDictionary(field => field.Key, field => Plain(field.Value)), StringComparer.Ordinal),
        ValueKind.List => value.Items.Select(Plain).ToList(),
        _ => value.Scalar,
    };

    [GeneratedRegex("\"\\w*Line\":\\d+,?")]
    private static partial Regex LineNumbers();

    [Fact]
    public void TheReadableOchnoIsTheFullOchno() => Assert.Equal(Loaded(Load(FullOchno)), Loaded(Load(ReadableOchno)));

    [Fact]
    public void BusDeviceAndPlatformScopesAreTheFullFormsRules()
    {
        var readable = Load(ReadableRoom, "room");

        // V4Tests.Room writes one platform as "on: windows/intel", not a list: the same rule, so only what is read is compared.
        Assert.Equal(Loaded(HardwareDatabase.LoadV4(Files(("guides/room.yaml", V4Tests.Room))).Guides["room"], root: false), Loaded(readable, root: false));
        Assert.Equal(["room|||", "chain|||", "chain|usb2||", "chain||audio|", "chain||camera|", "chain|||windows/intel"], readable.Rules.Select(rule => rule.Selector));
    }

    [Fact]
    public void CustomCountingWithAWeightAComputerHubCountAndActiveCablesAreTheFullForm() =>
        Assert.Equal(Loaded(Load(FullCustom)), Loaded(Load(ReadableCustom)));

    // The same rules judge the same (V4Tests.ABusRuleAppliesOnlyOnItsBus): the usb2 limit applies on USB 2 only.
    [Fact]
    public void AReadableBusRuleJudgesAsTheFullForm()
    {
        var db = HardwareDatabase.LoadV4(Files(("guides/room.yaml", ReadableRoom)));
        var tree = new[] { Controller("C1", RootWithBuses("ROOT", Hub("TWO1", Hub("TWO2", Device("USB2DEV"))), Hub("THREE1", Hub("THREE2", Device("USB3DEV"))))) };

        var report = DiagnosticEvaluator.Evaluate(tree, db.Platforms["macos-apple-silicon"], [], externalDisplays: 0, guide: db.Guides["room"]);

        Assert.Equal(ConnectionStatus.Purple, report.Connections["USB2DEV"]);
        Assert.Equal(ConnectionStatus.Green, report.Connections["USB3DEV"]);
        Assert.Contains(report.Findings, finding => finding.SubjectId == "USB2DEV" && finding.Message.StartsWith("Room: chain 3 exceeds the maximum 2.", StringComparison.Ordinal));
    }

    [Fact]
    public void AUsb3ScopeAPlatformWithoutAChipAndADefaultUnknownAreRead()
    {
        var guide = Load(ReadableRoom.Replace("    usb2:\n", "    usb3:\n", StringComparison.Ordinal).Replace("on windows/intel:", "on windows:", StringComparison.Ordinal)
            .Replace("  if unknown: at least\n", "", StringComparison.Ordinal), "room");

        Assert.Contains(guide.Rules, rule => rule.Bus == "usb3" && rule.Max == 2);
        Assert.Contains(guide.Rules, rule => rule.On.SequenceEqual(["windows"]));
        Assert.Null(guide.MeasureDefinitions["chain"].ComputerHubUnknown);
    }

    [Fact]
    public void ScopesNest()
    {
        var rules = Load(ReadableRoom.Replace("    cameras:\n", "    on windows/amd:\n      cameras:\n        max: 2\n        source: room\n        quote: two for cameras on AMD\n    cameras:\n", StringComparison.Ordinal), "room").Rules;

        var nested = Assert.Single(rules, rule => rule.On.Count > 0 && rule.Devices.Count > 0);
        Assert.Equal(["windows/amd"], nested.On);
        Assert.Equal(["camera"], nested.Devices);
        Assert.Equal(2L, nested.Max);
    }

    [Fact]
    public void LimitsAndActiveCablesMustHaveLinesBelowThem()
    {
        const string Own = "name: Own\nselectable: true\nproav: 0\ncounts: tiers\n";

        var limits = Error(Own + "limits:\n  - tiers\n");
        Assert.Equal(5, limits.Line);
        Assert.Equal("'limits' has each count on a line below it, with its recommended, max, quote and source.", limits.Message);
        Assert.StartsWith("'active cables' has usb2 and usb3", Error(Own + "active cables: 5\nlimits:\n  tiers:\n    max: 7\n").Message, StringComparison.Ordinal);
    }

    // Empty means not stated: the rule has only the other number.
    [Fact]
    public void AnEmptyNumberIsNotStated()
    {
        var rule = Load(ReadableRoom.Replace("    max: 4\n", "    max:\n    recommended: 3\n", StringComparison.Ordinal), "room").Rules[0];

        Assert.Equal((null, 3L, false), (rule.Max, rule.Recommended, rule.MaxNone));
    }

    // A newer readable file, with a scope this app does not know, needs a newer app: listed, not an error.
    [Fact]
    public void ARequiresInTheReadableLayoutStillSkipsTheGuide()
    {
        var guide = Load(ReadableOchno.Replace("proav: 1", "proav: 1\nrequires:\n  - laser_link", StringComparison.Ordinal)
            .Replace("  room:\n", "  room:\n    usb4:\n      max: 2\n", StringComparison.Ordinal));

        Assert.True(guide.NeedsNewerApp);
        Assert.Equal(["laser_link"], guide.Unsupported);
    }

    [Theory]
    [InlineData("    max: 5\n", "    max: five\n", 22, "Limit 'chain': 'max' must be a whole number or none.")]
    [InlineData("    max: 4\n", "    max: 4.5\n", 18, "Limit 'room': 'max' must be a whole number or none.")]
    [InlineData("    max: 5\n", "    max: -1\n", 22, "Limit 'chain': 'max' must be a whole number or none.")]
    [InlineData("  room:\n    max: 4\n", "  rooom:\n    max: 4\n", 17, "'rooom' is not a standard count in base.yaml (chain, tiers, room)")]
    [InlineData("  room:\n    max: 4\n", "  room:\n", 17, "Limit 'room': give 'recommended', 'max' or both.")]
    [InlineData("    max: 5\n    source: room\n", "", 22, "Limit 'chain': a quote or source belongs with a 'recommended' or 'max'")]
    [InlineData("    max: 5\n    source: room\n    quote: >\n      five hubs\n      in a chain\n", "    source: room\n", 22, "Limit 'chain': a quote or source belongs with a 'recommended' or 'max'")]
    [InlineData("counts: room, chain", "counts:\n  room: 1", 12, "'counts' names the standard counts on one line")]
    [InlineData("    usb2:\n", "    usb3:\n      usb2:\n        max: 1\n    usb2:\n", 28, "Limit 'chain', usb3, usb2: a scope cannot sit inside another bus scope")]
    [InlineData("    audio:\n", "    audio:\n      cameras:\n        max: 1\n", 32, "Limit 'chain', audio, cameras: a scope cannot sit inside another device scope")]
    [InlineData("    on windows/intel:\n", "    on macos:\n      on windows/intel:\n        max: 1\n    on windows/intel:\n", 40, "Limit 'chain', on macos, on windows/intel: a scope cannot sit inside another platform scope")]
    [InlineData("    usb2:\n", "    usb9:\n", 27, "Limit 'chain': unknown field 'usb9'.")]
    [InlineData("counts: room, chain", "counts: room, hops", 12, "'hops' is not a standard count in base.yaml (chain, tiers, room); write it out under 'measures'.")]
    [InlineData("  if unknown: at least", "  if unknown: maybe", 15, "'computer_hubs: if unknown' must be 'at least' or a number of hubs.")]
    public void AMistakeNamesTheReadableKeyAndItsLine(string from, string to, int line, string message)
    {
        var error = Error(ReadableRoom.Replace(from, to, StringComparison.Ordinal));

        Assert.Equal("guides/guide.yaml", error.File);
        Assert.Equal(line, error.Line);
        Assert.StartsWith(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileInTheFullFormIsLeftAsItIs() => Assert.False(GuideLayout.IsReadable(ProfileFileReader.Read(FullOchno, "guides/ochno.yaml", [])!));

    // Every v4 philosophy in the readable layout (#386): block style only, and a comment on every key.
    [Fact]
    public void TheReadablePhilosophiesHaveNoBracesOrEscapesAndAKeyCommentOnEveryKey()
    {
        var folder = Path.Combine(RealDatabaseV4Tests.Folder(), "guides");
        foreach (var path in Directory.EnumerateFiles(folder, "*.yaml"))
        {
            var text = File.ReadAllText(path);
            if (!GuideLayout.IsReadable(ProfileFileReader.Read(text, path, [])!))
            {
                continue;
            }
            Assert.Empty(LintProblems(text).Select(problem => $"{Path.GetFileName(path)} {problem}"));
        }
    }

    [Fact]
    public void TheLintFindsBracesEscapesAndKeysWithoutAComment()
    {
        Assert.Equal(["line 1: no comment on 'name'", "line 2: '{', '[' or '\\\"'"], LintProblems("name: A\nbasis: { a: \"x\" }  # b\n"));
        Assert.Empty(LintProblems("notes:  # n\n  - >\n    text: with [brackets]\n    and \"quotes\"\n"));
    }

    // The structure lines of a file: keys and list items, not comments or the text of a '>' or '|' block.
    private static List<string> LintProblems(string text)
    {
        var problems = new List<string>();
        var blockIndent = -1;
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var indent = line.Length - line.TrimStart().Length;
            if (line.Trim().Length == 0 || line.TrimStart().StartsWith('#'))
            {
                continue;
            }
            if (blockIndent >= 0 && indent > blockIndent)
            {
                continue;
            }
            blockIndent = -1;
            var code = line.Split("  #")[0].TrimEnd();
            if (code.IndexOfAny(['{', '[']) >= 0 || code.Contains("\\\"", StringComparison.Ordinal))
            {
                problems.Add($"line {i + 1}: '{{', '[' or '\\\"'");
            }
            if (KeyLine().Match(code) is { Success: true } key && !line.Contains("  #", StringComparison.Ordinal))
            {
                problems.Add($"line {i + 1}: no comment on '{key.Groups[1].Value}'");
            }
            if (code.EndsWith('>') || code.EndsWith('|'))
            {
                blockIndent = indent;
            }
        }
        return problems;
    }

    [GeneratedRegex(@"^\s*(?:- )?([A-Za-z0-9_/ .-]+):(?: |$)")]
    private static partial Regex KeyLine();
}
