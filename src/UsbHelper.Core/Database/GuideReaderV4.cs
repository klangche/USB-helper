using UsbHelper.Core.Topology;

namespace UsbHelper.Core.Database;

/// <summary>
/// Reads a v4 guide (#327): <c>basis</c>, <c>sources</c>, <c>measures</c>, <c>rules</c>, <c>requires</c> and, per
/// platform and chip, <c>mst</c>. There is no fallback between guides: a guide judges only what its rules state.
/// </summary>
internal static class GuideReaderV4
{
    /// <summary>The path elements this app can count (<see cref="PathElements"/>).</summary>
    public static readonly IReadOnlyList<string> KnownElements =
        [PathElements.RootHub, PathElements.ComputerHub, PathElements.Hub, PathElements.MarkedHub, PathElements.CableLength,
            PathElements.Usb4Router, PathElements.Device];

    /// <summary>The device kinds this app tells apart, by USB class (#274).</summary>
    public static readonly IReadOnlyList<string> KnownDeviceKinds = ["camera", "audio", "hid"];

    /// <summary>Evidence a measure may accept for <c>computer_hub</c>.</summary>
    public static readonly IReadOnlyList<string> ComputerHubEvidence = ["detected", "computer", "chip"];

    private static readonly string[] TopFields =
        ["name", "selectable", "proav", "basis", "sources", "measures", "rules", "requires", "platforms", "notes", "verification"];

    /// <param name="baseElements">The path elements base.yaml lists.</param>
    /// <param name="baseKinds">The device kinds base.yaml lists.</param>
    /// <param name="chips">Every platform's chip keys, for <c>platforms</c> and <c>on</c>.</param>
    public static Guide? Read(
        ProfileValue root, string id, IReadOnlyCollection<string> baseElements, IReadOnlyCollection<string> baseKinds,
        IReadOnlyDictionary<string, List<string>> chips, List<DatabaseError> errors)
    {
        var file = root.File;
        void Error(ProfileValue at, string message) => errors.Add(new DatabaseError(file, at.Line, message));

        var name = root.Find("name")?.Scalar as string;
        var selectable = root.Find("selectable")?.Scalar as bool?;
        if (string.IsNullOrEmpty(name))
        {
            Error(root.Find("name") ?? root, "Field 'name' is required and must be text.");
        }
        if (selectable == null)
        {
            Error(root.Find("selectable") ?? root, "Field 'selectable' is required and must be true or false.");
        }
        if (root.Find("proav")?.Scalar is not (0L or 1L))
        {
            Error(root.Find("proav") ?? root, "Field 'proav' is required and must be 1 (ProAV) or 0 (general purpose).");
        }
        if (string.IsNullOrEmpty(name) || selectable == null)
        {
            return null;
        }

        // A guide that needs what this app does not know is listed, not judged, and not checked further: a newer
        // database must not break an older app.
        var unsupported = Texts(root.Find("requires"), "requires", Error)
            .Where(each => !KnownElements.Contains(each) && !KnownDeviceKinds.Contains(each)).ToList();
        if (unsupported.Count > 0)
        {
            return new Guide(id, name, selectable.Value, root, null) { Format = 4, Unsupported = unsupported };
        }

        foreach (var (key, value) in root.Fields.Where(field => !TopFields.Contains(field.Key)))
        {
            Error(value, $"Unknown field '{key}'.");
        }
        // Every guide but USB-Helper's own states someone else's limits, so each of its rules quotes its source.
        var manufacturer = id != Guide.DefaultId;
        var sources = ReadSources(root.Find("sources"), Error);
        if (root.Find("basis") is { } basis)
        {
            CheckQuoted(basis, "basis", sources, required: true, Error);
        }
        else if (manufacturer)
        {
            Error(root, "Field 'basis' is required in a guide from a source: the source and the exact quote the guide works from.");
        }
        var measures = ReadMeasures(root.Find("measures"), baseElements, Error);
        var rules = ReadRules(root.Find("rules"), measures, baseKinds, chips, sources, manufacturer, Error);
        CheckPlatforms(root.Find("platforms"), chips, Error);
        return new Guide(id, name, selectable.Value, root, null)
        {
            Format = 4,
            MeasureDefinitions = measures,
            Rules = rules,
            SourceDefinitions = sources,
        };
    }

    private static Dictionary<string, GuideSource> ReadSources(ProfileValue? value, Action<ProfileValue, string> error)
    {
        var sources = new Dictionary<string, GuideSource>();
        if (value == null)
        {
            return sources;
        }
        if (value.Kind != ValueKind.Mapping)
        {
            error(value, "Field 'sources': expected a mapping of id to link, title, author, version and read.");
            return sources;
        }
        string[] fields = ["link", "title", "author", "version", "read"];
        foreach (var (id, source) in value.Fields)
        {
            if (source.Kind != ValueKind.Mapping)
            {
                error(source, $"Source '{id}': expected a mapping.");
                continue;
            }
            foreach (var (key, field) in source.Fields)
            {
                if (!fields.Contains(key))
                {
                    error(field, $"Source '{id}': unknown field '{key}'.");
                }
                else if (field.Kind is not (ValueKind.String or ValueKind.Null))
                {
                    error(field, $"Source '{id}': '{key}' must be text.");
                }
            }
            if (source.Find("link")?.Scalar is string link && Guide.WebLink(link) == null)
            {
                error(source.Find("link")!, $"Source '{id}': 'link' must be an http or https address.");
            }
            sources[id] = new GuideSource(id, source.Find("link")?.Scalar as string, source.Find("title")?.Scalar as string,
                source.Find("author")?.Scalar as string, source.Find("version")?.Scalar as string, source.Find("read")?.Scalar as string);
        }
        return sources;
    }

    private static Dictionary<string, GuideMeasure> ReadMeasures(
        ProfileValue? value, IReadOnlyCollection<string> baseElements, Action<ProfileValue, string> error)
    {
        var measures = new Dictionary<string, GuideMeasure>();
        if (value is not { Kind: ValueKind.Mapping })
        {
            error(value ?? ProfileValue.CreateScalar(ValueKind.Null, null, "", 0), "Field 'measures' is required: each measure with 'counts'.");
            return measures;
        }
        foreach (var (id, measure) in value.Fields)
        {
            if (measure.Kind != ValueKind.Mapping)
            {
                error(measure, $"Measure '{id}': expected a mapping with 'counts'.");
                continue;
            }
            foreach (var (key, field) in measure.Fields.Where(field => field.Key is not ("counts" or "weights" or "computer_hub" or "cable_length")))
            {
                error(field, $"Measure '{id}': unknown field '{key}'.");
            }
            var counts = Texts(measure.Find("counts"), $"measures.{id}.counts", error).ToList();
            if (counts.Count == 0)
            {
                error(measure, $"Measure '{id}': 'counts' must list at least one path element.");
            }
            foreach (var element in counts.Where(element => !KnownElements.Contains(element) || !baseElements.Contains(element)))
            {
                error(measure.Find("counts")!, $"Measure '{id}': unknown path element '{element}'.");
            }
            var weights = new Dictionary<string, int>();
            if (measure.Find("weights") is { } weightValue)
            {
                foreach (var (element, weight) in weightValue.Fields)
                {
                    if (!counts.Contains(element) || weight.Scalar is not long number || number < 0)
                    {
                        error(weight, $"Measure '{id}': weight '{element}' must be a whole number for an element it counts.");
                        continue;
                    }
                    weights[element] = (int)number;
                }
            }
            List<string> from = [.. ComputerHubEvidence];
            int? unknown = null;
            var unknownLine = 0;
            if (measure.Find("computer_hub") is { } computerHub)
            {
                from = [.. Texts(computerHub.Find("from"), $"measures.{id}.computer_hub.from", error)];
                foreach (var each in from.Where(each => !ComputerHubEvidence.Contains(each)))
                {
                    error(computerHub, $"Measure '{id}': computer_hub evidence '{each}' must be one of {string.Join(", ", ComputerHubEvidence)}.");
                }
                var unknownValue = computerHub.Find("unknown");
                if (unknownValue?.Scalar is long number && number >= 0)
                {
                    unknown = (int)number;
                    unknownLine = unknownValue.Line;
                }
                else if (unknownValue?.Scalar is not "open")
                {
                    error(unknownValue ?? computerHub, $"Measure '{id}': computer_hub 'unknown' must be 'open' or a whole number.");
                }
            }
            if (measure.Find("cable_length") is { } cable)
            {
                foreach (var (key, field) in cable.Fields)
                {
                    if (key is not ("usb2_metres_per_tier" or "usb3_metres_per_tier") || field.Scalar is not long and not double)
                    {
                        error(field, $"Measure '{id}': cable_length takes usb2_metres_per_tier and usb3_metres_per_tier, each a number.");
                    }
                }
            }
            measures[id] = new GuideMeasure(id, counts, weights, from, unknown, unknownLine);
        }
        return measures;
    }

    private static List<GuideRule> ReadRules(
        ProfileValue? value, IReadOnlyDictionary<string, GuideMeasure> measures, IReadOnlyCollection<string> baseKinds,
        IReadOnlyDictionary<string, List<string>> chips, IReadOnlyDictionary<string, GuideSource> sources, bool manufacturer,
        Action<ProfileValue, string> error)
    {
        var rules = new List<GuideRule>();
        if (value is not { Kind: ValueKind.List })
        {
            error(value ?? ProfileValue.CreateScalar(ValueKind.Null, null, "", 0), "Field 'rules' is required: a list of rules.");
            return rules;
        }
        string[] fields = ["measure", "bus", "devices", "on", "recommended", "max", "source", "quote"];
        foreach (var rule in value.Items)
        {
            if (rule.Kind != ValueKind.Mapping)
            {
                error(rule, "A rule must be a mapping.");
                continue;
            }
            foreach (var (key, field) in rule.Fields.Where(field => !fields.Contains(field.Key)))
            {
                error(field, $"Rule: unknown field '{key}'.");
            }
            var measure = rule.Find("measure")?.Scalar as string;
            if (measure == null || !measures.ContainsKey(measure))
            {
                error(rule.Find("measure") ?? rule, $"Rule: 'measure' must be one of the guide's measures ({string.Join(", ", measures.Keys)}).");
                continue;
            }
            var bus = rule.Find("bus")?.Scalar as string;
            if (rule.Find("bus") is { Kind: not ValueKind.Null } && bus is not ("usb2" or "usb3"))
            {
                error(rule.Find("bus")!, "Rule: 'bus' must be usb2 or usb3.");
            }
            var devices = Texts(rule.Find("devices"), "devices", error).ToList();
            foreach (var kind in devices.Where(kind => !KnownDeviceKinds.Contains(kind) || !baseKinds.Contains(kind)))
            {
                error(rule.Find("devices")!, $"Rule: unknown device kind '{kind}'.");
            }
            var on = rule.Find("on") is { Kind: ValueKind.String } single ? [(string)single.Scalar!] : Texts(rule.Find("on"), "on", error).ToList();
            foreach (var each in on)
            {
                var parts = each.Split('/');
                if (!chips.TryGetValue(parts[0], out var platformChips) || parts.Length > 2 || (parts.Length == 2 && !platformChips.Contains(parts[1])))
                {
                    error(rule.Find("on")!, $"Rule: 'on' names '{each}', which is not a platform or platform/chip.");
                }
            }
            var (recommended, recommendedNone) = Limit(rule, "recommended", error);
            var (max, maxNone) = Limit(rule, "max", error);
            if (rule.Find("recommended") == null && rule.Find("max") == null)
            {
                error(rule, "Rule: give 'recommended', 'max' or both.");
            }
            var source = rule.Find("source")?.Scalar as string;
            var quote = rule.Find("quote")?.Scalar as string;
            CheckQuoted(rule, "Rule", sources, manufacturer, error);
            var parsed = new GuideRule(measure, bus, devices, on, recommended, max, recommendedNone, maxNone, source, quote, rule.File, rule.Line);
            if (rules.FirstOrDefault(other => other.Selector == parsed.Selector) is { } duplicate)
            {
                error(rule, $"Rule: the same measure, bus, devices and platforms as the rule on line {duplicate.Line}.");
                continue;
            }
            rules.Add(parsed);
        }
        return rules;
    }

    // A source id from sources and an exact quote: required in a manufacturer guide, else optional.
    private static void CheckQuoted(
        ProfileValue value, string what, IReadOnlyDictionary<string, GuideSource> sources, bool required, Action<ProfileValue, string> error)
    {
        var source = value.Find("source");
        var quote = value.Find("quote");
        if (required && (source?.Scalar is not string || quote?.Scalar is not string { Length: > 0 }))
        {
            error(value, $"{what}: 'source' and 'quote' are required in a guide from a source.");
        }
        if (source?.Scalar is string id && !sources.ContainsKey(id))
        {
            error(source, $"{what}: source '{id}' is not in 'sources'.");
        }
    }

    // recommended or max: a whole number, none, or not given.
    private static (long? Value, bool None) Limit(ProfileValue rule, string key, Action<ProfileValue, string> error)
    {
        var value = rule.Find(key);
        switch (value?.Scalar)
        {
            case null:
                return (null, false);
            case long number when number >= 0:
                return (number, false);
            case Guide.None:
                return (null, true);
            default:
                error(value, $"Rule: '{key}' must be a whole number or none.");
                return (null, false);
        }
    }

    // platforms.<platform>.mst and platforms.<platform>.chips.<chip>.mst, 1 or 0.
    private static void CheckPlatforms(ProfileValue? value, IReadOnlyDictionary<string, List<string>> chips, Action<ProfileValue, string> error)
    {
        foreach (var (platform, settings) in value?.Fields ?? new Dictionary<string, ProfileValue>())
        {
            if (!chips.TryGetValue(platform, out var platformChips))
            {
                error(settings, $"Unknown key 'platforms.{platform}'.");
                continue;
            }
            foreach (var (key, field) in settings.Fields)
            {
                if (key == "mst")
                {
                    CheckMst(field, error);
                }
                else if (key == "chips")
                {
                    foreach (var (chip, chipSettings) in field.Fields)
                    {
                        if (!platformChips.Contains(chip))
                        {
                            error(chipSettings, $"Unknown key 'platforms.{platform}.chips.{chip}'.");
                            continue;
                        }
                        foreach (var (chipKey, chipField) in chipSettings.Fields)
                        {
                            if (chipKey == "mst")
                            {
                                CheckMst(chipField, error);
                            }
                            else
                            {
                                error(chipField, $"Unknown key 'platforms.{platform}.chips.{chip}.{chipKey}'.");
                            }
                        }
                    }
                }
                else
                {
                    error(field, $"Unknown key 'platforms.{platform}.{key}'.");
                }
            }
        }
    }

    private static void CheckMst(ProfileValue value, Action<ProfileValue, string> error)
    {
        if (value.Scalar is not (null or 0L or 1L))
        {
            error(value, "Field 'mst' must be 1, 0 or empty.");
        }
    }

    // A list of text items; a missing value is an empty list.
    private static IEnumerable<string> Texts(ProfileValue? value, string field, Action<ProfileValue, string> error)
    {
        if (value == null || value.Kind == ValueKind.Null)
        {
            return [];
        }
        if (value.Kind != ValueKind.List || value.Items.Any(item => item.Kind != ValueKind.String))
        {
            error(value, $"Field '{field}': expected a list of text items.");
            return [];
        }
        return value.Items.Select(item => (string)item.Scalar!);
    }
}
