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

    /// <summary>The verification status the file writes; null when it writes none.</summary>
    public string? Status => Root.Find("verification.status")?.Scalar as string;

    /// <summary>The file is verified or partially verified.</summary>
    public bool Verified => Status is "verified" or "partial";

    /// <summary>The value at a dotted path such as <c>limits.tiers.max</c>, or null when the file has no such key.</summary>
    public ProfileValue? Find(string path) => Root.Find(path);

    /// <summary>
    /// A limit for one platform and chip, such as <c>tiers.max</c>, <c>camera_audio.tiers.recommended</c> or, on one bus,
    /// <c>usb2.hubs.max</c>. Tiers are the same on every bus, so a bus in front of them is ignored; hubs on a bus
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
