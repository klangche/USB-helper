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
        ["name", "selectable", "proav", "own", "basis", "sources", "measures", "rules", "requires", "platforms", "notes", "verification"];

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
        // A ProAV guide states a manufacturer's limits, so each rule quotes its source; own: true marks the owner's own
        // numbers (USB-Helper, Unisynk), which have no source to quote. A general-purpose guide (proav: 0) quotes where it can.
        if (root.Find("own") is { } ownValue && ownValue.Kind != ValueKind.Boolean)
        {
            Error(ownValue, "Field 'own' must be true or false.");
        }
        var manufacturer = root.Find("proav")?.Scalar is 1L && root.Find("own")?.Scalar is not true;
        if (root.Find("own")?.Scalar is true && root.Find("basis") != null)
        {
            Error(root.Find("basis")!, "A guide with own: true states the owner's own numbers, so it has no 'basis' to quote.");
        }
        var sources = ReadSources(root.Find("sources"), Error);
        if (root.Find("basis") is { } basis)
        {
            CheckQuoted(basis, "basis", sources, required: true, Error);
        }
        else if (manufacturer)
        {
            Error(root, "Field 'basis' is required in a manufacturer guide (proav: 1 without own: true): the source and the exact quote it works from.");
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
        string[] fields = ["link", "title", "author", "version", "read", "files", "quotes"];
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
                else if (key is not ("files" or "quotes") && field.Kind is not (ValueKind.String or ValueKind.Null))
                {
                    error(field, $"Source '{id}': '{key}' must be text.");
                }
            }
            if (source.Find("link")?.Scalar is string link && Guide.WebLink(link) == null)
            {
                error(source.Find("link")!, $"Source '{id}': 'link' must be an http or https address.");
            }
            var files = ReadFiles(id, source.Find("files"), error);
            // A plain link is one link entry, as before #333.
            if (files.Count == 0 && Guide.WebLink(source.Find("link")?.Scalar as string) is { } plain)
            {
                files = [new SourceFile(DefaultName(plain), plain, IsPdf: false, null, null)];
            }
            var quotes = ReadQuotes(id, source.Find("quotes"), files, error);
            sources[id] = new GuideSource(id, files.FirstOrDefault(file => !file.IsPdf && file.Url != null)?.Url?.ToString() ?? source.Find("link")?.Scalar as string,
                source.Find("title")?.Scalar as string, source.Find("author")?.Scalar as string, source.Find("version")?.Scalar as string,
                source.Find("read")?.Scalar as string)
            { Files = files, Quotes = quotes };
        }
        return sources;
    }

    // files (#333): at least one entry, each exactly one of pdf or link. Several entries each need a unique name.
    private static List<SourceFile> ReadFiles(string id, ProfileValue? value, Action<ProfileValue, string> error)
    {
        var files = new List<SourceFile>();
        if (value == null || value.Kind == ValueKind.Null)
        {
            return files;
        }
        if (value.Kind != ValueKind.List || value.Items.Count == 0)
        {
            error(value, $"Source '{id}': 'files' must list at least one entry.");
            return files;
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in value.Items)
        {
            if (entry.Kind != ValueKind.Mapping)
            {
                error(entry, $"Source '{id}': each file entry must be a mapping.");
                continue;
            }
            foreach (var (key, field) in entry.Fields.Where(field => field.Key is not ("name" or "pdf" or "link" or "sha256" or "size")))
            {
                error(field, $"Source '{id}': unknown file field '{key}'.");
            }
            var isPdf = entry.Fields.ContainsKey("pdf");
            if (isPdf == entry.Fields.ContainsKey("link"))
            {
                error(entry, $"Source '{id}': each file entry has exactly one of 'pdf' and 'link'.");
                continue;
            }
            var address = entry.Find(isPdf ? "pdf" : "link")?.Scalar as string;
            var url = Guide.WebLink(address);
            if ((address != null && url == null) || (!isPdf && url == null))
            {
                error(entry, $"Source '{id}': '{(isPdf ? "pdf" : "link")}' must be an http or https address{(isPdf ? ", or empty for a PDF without a public download" : "")}.");
                continue;
            }
            var sha256 = entry.Find("sha256")?.Scalar as string;
            if (sha256 != null && !System.Text.RegularExpressions.Regex.IsMatch(sha256, "^[0-9a-f]{64}$"))
            {
                error(entry.Find("sha256")!, $"Source '{id}': 'sha256' must be 64 lowercase hex digits.");
            }
            if (isPdf && url == null && sha256 == null)
            {
                error(entry, $"Source '{id}': a PDF without a URL needs its 'sha256', so a local copy can be checked.");
            }
            var name = entry.Find("name")?.Scalar as string;
            if (value.Items.Count > 1 && string.IsNullOrWhiteSpace(name))
            {
                error(entry, $"Source '{id}': with several entries, every entry needs its own 'name'.");
                continue;
            }
            name ??= url != null ? DefaultName(url) : id;
            if (!names.Add(name))
            {
                error(entry, $"Source '{id}': the name '{name}' is used twice.");
                continue;
            }
            if (entry.Find("size") is { Kind: not ValueKind.Null } sizeValue && sizeValue.Scalar is not (long and > 0))
            {
                error(sizeValue, $"Source '{id}': 'size' must be a whole number of bytes.");
            }
            files.Add(new SourceFile(name, url, isPdf, sha256, entry.Find("size")?.Scalar as long?));
        }
        return files;
    }

    // quotes (#333): each text with the entry's name and an optional page; with a single PDF, file may be left out.
    private static List<SourceQuote> ReadQuotes(string id, ProfileValue? value, IReadOnlyList<SourceFile> files, Action<ProfileValue, string> error)
    {
        var quotes = new List<SourceQuote>();
        foreach (var quote in value?.Items ?? [])
        {
            var text = quote.Find("text")?.Scalar as string;
            var file = quote.Find("file")?.Scalar as string;
            if (string.IsNullOrEmpty(text))
            {
                error(quote, $"Source '{id}': a quote needs 'text'.");
                continue;
            }
            var pdfs = files.Where(each => each.IsPdf).ToList();
            if (file == null && pdfs.Count == 1)
            {
                file = pdfs[0].Name;
            }
            else if (file == null)
            {
                error(quote, $"Source '{id}': a quote needs 'file', the name of its entry, unless the source has exactly one PDF.");
            }
            if (quote.Find("page") is { Kind: not ValueKind.Null } pageValue && pageValue.Scalar is not (long and > 0))
            {
                error(pageValue, $"Source '{id}': a quote's 'page' must be a page number from 1.");
            }
            if (file != null && files.All(each => each.Name != file))
            {
                error(quote.Find("file") ?? quote, $"Source '{id}': the quote's file '{file}' is not one of its entries.");
            }
            quotes.Add(new SourceQuote(text, file, quote.Find("page")?.Scalar is long page ? (int)page : null));
        }
        return quotes;
    }

    /// <summary>The name of a source's only entry without one (#333): the URL's last path part, decoded, or else its host.</summary>
    internal static string DefaultName(Uri url)
    {
        var last = url.AbsolutePath.TrimEnd('/').Split('/')[^1];
        return last.Length == 0 ? url.Host : Uri.UnescapeDataString(last).Replace('+', ' ');
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
            var metres = new Dictionary<string, double>();
            if (measure.Find("cable_length") is { } cable)
            {
                foreach (var (key, field) in cable.Fields)
                {
                    if (key is not ("usb2_metres_per_tier" or "usb3_metres_per_tier") || field.Scalar is not (long or double)
                        || Convert.ToDouble(field.Scalar, System.Globalization.CultureInfo.InvariantCulture) <= 0)
                    {
                        error(field, $"Measure '{id}': cable_length takes usb2_metres_per_tier and usb3_metres_per_tier, each a number above 0.");
                        continue;
                    }
                    metres[key[..4]] = Convert.ToDouble(field.Scalar, System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            measures[id] = new GuideMeasure(id, counts, weights, from, unknown, unknownLine) { CableMetresPerTier = metres };
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
            error(value, $"{what}: 'source' and 'quote' are required in a manufacturer guide (proav: 1 without own: true).");
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
