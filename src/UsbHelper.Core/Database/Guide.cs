namespace UsbHelper.Core.Database;

/// <summary>
/// A guide (<c>guides/&lt;id&gt;.yaml</c>): the limits and recommendations of one guide, such as PureLink, in one file
/// (owner decision 2026-11-17, #285). Global <c>limits</c>, then per platform <c>mst</c> and <c>limits</c>, then per chip
/// the same. An empty value falls back to the value above it (chip, platform, global), then to USB-Helper's
/// (<c>guides/usb-helper.yaml</c>, the default guide) the same way; <c>none</c> states no limit.
/// </summary>
public sealed class Guide(string id, string name, bool selectable, ProfileValue root, Guide? fallback)
{
    /// <summary>The default guide's id: ticked at start, and the fallback for every empty value in another guide.</summary>
    public const string DefaultId = "usb-helper";

    /// <summary>The most characters an <c>exceeded_note</c> may have (owner decision 2026-11-17, #285).</summary>
    public const int ExceededNoteMaxLength = 160;

    /// <summary>The value that states no limit.</summary>
    public const string None = "none";

    /// <summary>Hubs daisy-chained in front of a device: the external hubs in front of it plus the chip's built-in hub levels.</summary>
    public const string Chain = "chain";

    /// <summary>USB tiers as the specification counts them (host = tier 1), built-in hub levels included.</summary>
    public const string Tiers = "tiers";

    /// <summary>External hubs per bus.</summary>
    public const string Hubs = "hubs";

    /// <summary>What a guide's <c>count</c> may list (owner decision 2026-11-18, #310).</summary>
    public static readonly IReadOnlyList<string> Measures = [Chain, Tiers, Hubs];

    /// <summary>The file's name without extension, for example <c>purelink</c> for <c>guides/purelink.yaml</c>.</summary>
    public string Id { get; } = id;

    /// <summary>The guide's <c>name</c>, for example <c>PureLink</c>.</summary>
    public string Name { get; } = name;

    /// <summary>False for a guide that is listed but not filled in yet; it is not offered as a choice.</summary>
    public bool Selectable { get; } = selectable;

    /// <summary>The guide's file, for example <c>guides/purelink.yaml</c>.</summary>
    public string File => Root.File;

    /// <summary>The file's fields. Each value knows its line.</summary>
    public ProfileValue Root { get; } = root;

    /// <summary>USB-Helper's guide, whose values apply where this one is empty; null for USB-Helper's own.</summary>
    public Guide? Fallback { get; } = fallback;

    /// <summary>The files the guide was read from: its one file.</summary>
    public IReadOnlyList<string> Files => [File];

    /// <summary><c>proav: 1</c>: a guide for ProAV rooms, marked in the menu; 0 for a general-purpose one (#299).</summary>
    public bool ProAv => Root.Find("proav")?.Scalar is 1L;

    /// <summary>
    /// What the guide counts (#310): its <c>count</c>, any of <see cref="Chain"/>, <see cref="Tiers"/> and <see cref="Hubs"/>.
    /// Only these are judged; numbers for the others are ignored. Read from this guide only, never from USB-Helper's.
    /// </summary>
    public IReadOnlyList<string> Count => Format >= 4
        ? [.. Rules.Select(rule => rule.Measure).Distinct()]
        : [.. Root.Find("count")?.Items.Select(item => item.Scalar).OfType<string>() ?? []];

    /// <summary>The guide counts <paramref name="measure"/> (<see cref="Measures"/>), so its limits for it are judged.</summary>
    public bool Counts(string measure) => Format < 4 && Count.Contains(measure);

    /// <summary>
    /// The guide's page: its <c>link</c> (#310), else the first web link among its <c>sources</c>, such as PureLink's
    /// whitepaper; null when there is none.
    /// </summary>
    public Uri? Link => WebLink(Root.Find("link")?.Scalar as string)
        ?? SourceDefinitions.Values.Select(source => WebLink(source.Link)).FirstOrDefault(uri => uri != null)
        ?? Root.Find("sources")?.Items
        .Select(item => WebLink((item.Scalar as string)?.Trim().Split(' ', 2)[0]))
        .FirstOrDefault(uri => uri != null);

    /// <summary>An absolute http or https address; null for anything else.</summary>
    internal static Uri? WebLink(string? text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri : null;

    /// <summary>The verification status the file writes; null when it writes none.</summary>
    public string? Status => Root.Find("verification.status")?.Scalar as string;

    /// <summary>The file is verified or partially verified.</summary>
    public bool Verified => Status is "verified" or "partial";

    /// <summary>The database format the guide was read from: 3 (limits) or 4 (measures and rules, #327).</summary>
    public int Format { get; init; } = 3;

    /// <summary>A v4 guide's measures by id (#327); empty for a v3 guide.</summary>
    public IReadOnlyDictionary<string, GuideMeasure> MeasureDefinitions { get; init; } = new Dictionary<string, GuideMeasure>();

    /// <summary>A v4 guide's rules, in file order (#327); empty for a v3 guide.</summary>
    public IReadOnlyList<GuideRule> Rules { get; init; } = [];

    /// <summary>A v4 guide's sources by id (#327); empty for a v3 guide.</summary>
    public IReadOnlyDictionary<string, GuideSource> SourceDefinitions { get; init; } = new Dictionary<string, GuideSource>();

    /// <summary>
    /// What the guide <c>requires</c> that this app does not know (#327). When not empty, the guide is listed as "needs a
    /// newer USB-Helper" and nothing is judged by it.
    /// </summary>
    public IReadOnlyList<string> Unsupported { get; init; } = [];

    /// <summary>The guide needs a newer USB-Helper (<see cref="Unsupported"/>).</summary>
    public bool NeedsNewerApp => Unsupported.Count > 0;

    /// <summary>The value at a dotted path such as <c>limits.tiers.max</c>, or null when the file has no such key.</summary>
    public ProfileValue? Find(string path) => Root.Find(path);

    /// <summary>
    /// A limit for one platform and chip, such as <c>tiers.max</c>, <c>chain.max</c>, <c>camera_audio.tiers.recommended</c> or,
    /// on one bus, <c>usb2.hubs.max</c>. Tiers and chain are the same on every bus, so a bus in front of them is ignored; hubs on a bus
    /// are <c>buses.&lt;bus&gt;.hubs</c>, else <c>hubs</c>, at each level. Looked up in the chip's limits, the platform's,
    /// the global ones, then USB-Helper's the same way; the first that is not empty. Null when every one is empty.
    /// </summary>
    public GuideValue? Limit(string platform, string chip, string path)
    {
        var parts = path.Split('.');
        if (parts[0] is "usb2" or "usb3" or "usb4")
        {
            var rest = string.Join('.', parts[1..]);
            return parts[1] == "hubs"
                ? Resolve(LimitLevels(platform, chip), [$"buses.{parts[0]}.{rest}", rest])
                : Resolve(LimitLevels(platform, chip), [rest]);
        }
        return Resolve(LimitLevels(platform, chip), [path]);
    }

    /// <summary>Whether the platform or chip extends the desktop over MST (1 or 0): the chip's <c>mst</c>, else the platform's.</summary>
    public GuideValue? Mst(string platform, string chip) =>
        Resolve([$"platforms.{platform}.chips.{chip}", $"platforms.{platform}"], ["mst"]);

    /// <summary>
    /// The <c>exceeded_note</c> to show with a finding about a limit this guide set: the chip's, the platform's or the
    /// global note, else USB-Helper's the same way. Null when none.
    /// </summary>
    public string? ExceededNote(string platform, string chip) =>
        Resolve(LimitLevels(platform, chip), ["exceeded_note"])?.Value.Scalar as string;

    private static string[] LimitLevels(string platform, string chip) =>
        [$"platforms.{platform}.chips.{chip}.limits", $"platforms.{platform}.limits", "limits"];

    // Each guide (this one, then USB-Helper's), each level from the most specific, each field in order.
    private GuideValue? Resolve(string[] levels, string[] fields)
    {
        for (var guide = this; guide != null; guide = guide.Fallback)
        {
            foreach (var level in levels)
            {
                foreach (var field in fields)
                {
                    var path = $"{level}.{field}";
                    if (guide.Find(path) is { Kind: not ValueKind.Null } value)
                    {
                        return new GuideValue(guide, value, path);
                    }
                }
            }
        }
        return null;
    }
}

/// <summary>A value from a guide file, with the guide that set it (the picked guide or USB-Helper's) and its key.</summary>
public sealed record GuideValue(Guide Guide, ProfileValue Value, string Path)
{
    /// <summary>The number; null for <c>none</c>.</summary>
    public long? Number => Value.Scalar as long?;
}
