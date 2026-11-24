namespace UsbHelper.Core.Database;

/// <summary>
/// The readable layout of a v4 philosophy (#386): a shorter way to write the same rules, expanded here into the full
/// form (<c>measures</c> and <c>rules</c>) before the guide is read, so both forms load and judge the same. A file in the
/// full form is left as it is.
/// </summary>
/// <example><code>
/// counts: chain
/// computer_hubs:
///   use: measured, computer, chip
///   if unknown: at least
/// limits:
///   chain:
///     max: 5
///     quote: having max 5 usb hubs daisy-chained
/// </code></example>
internal static class GuideLayout
{
    private const string Counts = "counts";
    private const string ComputerHubs = "computer_hubs";
    private const string ActiveCables = "active cables";
    private const string Limits = "limits";

    // Scope keys inside a limit: they make a rule for one bus or device kind; "on <os>" or "on <os>/<chip>" for a platform.
    private static readonly Dictionary<string, (string Field, string Value)> Scopes = new()
    {
        ["usb2"] = ("bus", "usb2"),
        ["usb3"] = ("bus", "usb3"),
        ["cameras"] = ("devices", "camera"),
        ["audio"] = ("devices", "audio"),
    };

    /// <summary>True when the file uses the readable layout: it states <c>limits</c> or <c>counts</c>.</summary>
    public static bool IsReadable(ProfileValue root) => root.Find(Limits) != null || root.Find(Counts) != null;

    /// <param name="standard">The standard counts base.yaml defines (<c>counts</c>), each with the path elements it adds.</param>
    public static ProfileValue Expand(ProfileValue root, IReadOnlyDictionary<string, IReadOnlyList<string>> standard, Action<ProfileValue, string> error)
    {
        if (!IsReadable(root))
        {
            return root;
        }
        root = Trimmed(root);
        var file = root.File;
        ProfileValue Text(string text, ProfileValue at) => ProfileValue.CreateScalar(ValueKind.String, text, file, at.Line);
        ProfileValue List(IEnumerable<string> texts, ProfileValue at) => ProfileValue.CreateList([.. texts.Select(text => Text(text, at))], file, at.Line);
        ProfileValue Map(Dictionary<string, ProfileValue> fields, ProfileValue at) => ProfileValue.CreateMapping(fields, file, at.Line);
        IEnumerable<string> Names(ProfileValue value) => value.Kind == ValueKind.List
            ? value.Items.Select(item => item.Scalar as string).OfType<string>()
            : (value.Scalar as string ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        var fields = root.Fields.Where(field => field.Key is not (Counts or ComputerHubs or ActiveCables or Limits)).ToDictionary();

        // The evidence for the computer's own hubs (#326): "measured" is what the system detects in this computer.
        ProfileValue? computerHub = null;
        if (root.Find(ComputerHubs) is { } hubs)
        {
            if (hubs.Kind != ValueKind.Mapping || hubs.Find("use") is not { } use)
            {
                error(hubs, "'computer_hubs' says which evidence for the computer's own hubs to use: 'use:' (measured, computer, chip) and 'if unknown:' (at least, or a number).");
            }
            else
            {
                foreach (var (key, value) in hubs.Fields.Where(field => field.Key is not ("use" or "if unknown")))
                {
                    error(value, $"'computer_hubs': unknown field '{key}'; it has 'use' and 'if unknown'.");
                }
                var from = Names(use).Select(name => name == "measured" ? "detected" : name).ToList();
                var unknown = hubs.Find("if unknown") is { } given
                    ? given.Scalar is long count ? ProfileValue.CreateScalar(ValueKind.Integer, count, file, given.Line)
                    : given.Scalar is "at least" ? Text("open", given)
                    : null
                    : Text("open", hubs);
                if (unknown == null)
                {
                    error(hubs.Find("if unknown")!, "'computer_hubs: if unknown' must be 'at least' or a number of hubs.");
                }
                computerHub = Map(new() { ["from"] = List(from, use), ["unknown"] = unknown ?? Text("open", hubs) }, hubs);
            }
        }

        // An active cable adds a tier per this many metres after the first, on its bus (#331).
        ProfileValue? cableLength = null;
        if (root.Find(ActiveCables) is { } cables)
        {
            var metres = new Dictionary<string, ProfileValue>();
            foreach (var (bus, value) in cables.Fields)
            {
                if (bus is not ("usb2" or "usb3"))
                {
                    error(value, $"'active cables': '{bus}' must be usb2 or usb3, with the metres per tier.");
                    continue;
                }
                metres[$"{bus}_metres_per_tier"] = value;
            }
            cableLength = Map(metres, cables);
        }

        // The measures: the standard counts named in 'counts' or in 'limits', each with this philosophy's evidence.
        var measures = root.Find("measures")?.Fields.ToDictionary() ?? [];
        var limits = root.Find(Limits);
        var named = (root.Find(Counts) is { } countsValue ? Names(countsValue) : [])
            .Concat(limits?.Fields.Keys ?? []).Distinct().Where(name => !measures.ContainsKey(name));
        foreach (var name in named)
        {
            if (!standard.TryGetValue(name, out var adds))
            {
                error(root.Find(Counts) ?? limits ?? root,
                    $"'{name}' is not a standard count in base.yaml ({string.Join(", ", standard.Keys)}); write it out under 'measures'.");
                continue;
            }
            var measure = new Dictionary<string, ProfileValue> { ["counts"] = List(adds, root.Find(Counts) ?? limits!) };
            if (computerHub != null && adds.Contains("computer_hub"))
            {
                measure["computer_hub"] = computerHub;
            }
            if (cableLength != null && adds.Contains("cable_length"))
            {
                measure["cable_length"] = cableLength;
            }
            measures[name] = Map(measure, root.Find(Counts) ?? limits!);
        }
        if (measures.Count > 0)
        {
            fields["measures"] = Map(measures, root.Find("measures") ?? root);
        }

        // The rules: one for each limit and each of its scopes, in file order.
        var rules = root.Find("rules")?.Items.ToList() ?? [];
        // A limit's own numbers come first, then its scopes, so the rules keep the order they are written in.
        void Rule(string measure, ProfileValue limit, Dictionary<string, ProfileValue> scope, string where)
        {
            var rule = new Dictionary<string, ProfileValue>(scope) { ["measure"] = Text(measure, limit) };
            var scoped = new List<(ProfileValue Value, Dictionary<string, ProfileValue> Scope, string Where)>();
            foreach (var (key, value) in limit.Fields)
            {
                if (key is "recommended" or "max")
                {
                    if (value.Scalar is not (long or "none"))
                    {
                        error(value, $"{where}: '{key}' must be a number or none.");
                        continue;
                    }
                    rule[key] = value;
                }
                else if (key is "quote" or "source")
                {
                    rule[key] = value;
                }
                else if (Scopes.TryGetValue(key, out var kind))
                {
                    scoped.Add((value, Scoped(scope, kind.Field, kind.Value, value), $"{where}, {key}"));
                }
                else if (key.StartsWith("on ", StringComparison.Ordinal))
                {
                    scoped.Add((value, Scoped(scope, "on", key[3..].Trim(), value), $"{where}, {key}"));
                }
                else
                {
                    error(value, $"{where}: unknown field '{key}'. A limit has recommended, max, quote and source, and the scopes usb2, usb3, cameras, audio and 'on <os>' or 'on <os>/<chip>'.");
                }
            }
            if (rule.ContainsKey("recommended") || rule.ContainsKey("max"))
            {
                rules.Add(Map(rule, limit));
            }
            foreach (var (value, inner, at) in scoped)
            {
                if (value.Kind != ValueKind.Mapping)
                {
                    error(value, $"{at}: expected recommended, max, quote and source on the lines below it.");
                    continue;
                }
                Rule(measure, value, inner, at);
            }
        }
        Dictionary<string, ProfileValue> Scoped(Dictionary<string, ProfileValue> scope, string field, string value, ProfileValue at)
        {
            var next = new Dictionary<string, ProfileValue>(scope);
            next[field] = field == "bus" ? Text(value, at)
                : List([.. scope.GetValueOrDefault(field)?.Items.Select(item => (string)item.Scalar!) ?? [], value], at);
            return next;
        }
        foreach (var (measure, limit) in limits?.Fields ?? new Dictionary<string, ProfileValue>())
        {
            if (limit.Kind != ValueKind.Mapping)
            {
                error(limit, $"Limit '{measure}': expected recommended, max, quote and source on the lines below it.");
                continue;
            }
            Rule(measure, limit, [], $"Limit '{measure}'");
        }
        fields["rules"] = ProfileValue.CreateList(rules, file, limits?.Line ?? root.Line);
        return Map(fields, root);
    }

    // A text block ('>' or '|') ends with a line break: the readable layout drops it, so a quote reads as in the full form.
    private static ProfileValue Trimmed(ProfileValue value) => value.Kind switch
    {
        ValueKind.String when value.Scalar is string text => ProfileValue.CreateScalar(ValueKind.String, text.TrimEnd(), value.File, value.Line),
        ValueKind.List => ProfileValue.CreateList([.. value.Items.Select(Trimmed)], value.File, value.Line),
        ValueKind.Mapping => ProfileValue.CreateMapping(value.Fields.ToDictionary(field => field.Key, field => Trimmed(field.Value)), value.File, value.Line),
        _ => value,
    };

    /// <summary>The standard counts in base.yaml's <c>counts</c> (#386): each with the path elements it adds.</summary>
    public static Dictionary<string, IReadOnlyList<string>> Standard(ProfileValue? counts) =>
        counts?.Fields.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<string>)[.. (entry.Value.Find("adds")?.Scalar as string ?? "")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)])
        ?? [];
}
