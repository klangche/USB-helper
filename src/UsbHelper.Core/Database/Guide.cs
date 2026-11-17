namespace UsbHelper.Core.Database;

/// <summary>
/// A guide (<c>guides/&lt;id&gt;/</c>): the limits and recommendations of one guide, such as PureLink, in small files:
/// <c>guide.yaml</c>, <c>all.yaml</c> and one file per platform with a block per chip. Every guide has the same files. An empty value falls back to USB-Helper's
/// (<c>guides/usb-helper/</c>, the default guide); <c>none</c> states no limit.
/// </summary>
public sealed class Guide(string id, string name, bool selectable, ProfileValue root, Guide? fallback)
{
    /// <summary>The default guide's id: ticked at start, and the fallback for every empty value in another guide.</summary>
    public const string DefaultId = "usb-helper";

    /// <summary>The most characters an <c>exceeded_note</c> may have (owner decision 2026-11-17, #285).</summary>
    public const int ExceededNoteMaxLength = 160;

    /// <summary>The value that states no limit.</summary>
    public const string None = "none";

    /// <summary>The folder's name, for example <c>purelink</c> for <c>guides/purelink/</c>.</summary>
    public string Id { get; } = id;

    /// <summary>The guide's <c>name</c>, for example <c>PureLink</c>.</summary>
    public string Name { get; } = name;

    /// <summary>False for a guide that is listed but not filled in yet; it is not offered as a choice.</summary>
    public bool Selectable { get; } = selectable;

    /// <summary>The header file, for example <c>guides/purelink/guide.yaml</c>; limits name their own file.</summary>
    public string File => Root.File;

    /// <summary>The file's fields. Each value knows its line.</summary>
    public ProfileValue Root { get; } = root;

    /// <summary>USB-Helper's guide, whose values apply where this one is empty; null for USB-Helper's own.</summary>
    public Guide? Fallback { get; } = fallback;

    /// <summary>Every file of the guide's folder that the guide was read from, guide.yaml first.</summary>
    public IReadOnlyList<string> Files => [.. FilesOf(Root).Prepend(File).Distinct()];

    private static IEnumerable<string> FilesOf(ProfileValue value) =>
        value.Fields.Values.Concat(value.Items).SelectMany(FilesOf).Prepend(value.File);

    /// <summary>The verification status the file writes; null when it writes none.</summary>
    public string? Status => Root.Find("verification.status")?.Scalar as string;

    /// <summary>The file is verified or partially verified.</summary>
    public bool Verified => Status is "verified" or "partial";

    /// <summary>The value at a dotted path such as <c>limits.tiers.max</c>, or null when the file has no such key.</summary>
    public ProfileValue? Find(string path) => Root.Find(path);

    /// <summary>
    /// A limit such as <c>usb2.hops.max</c> for one platform and chip: this guide's chip value (in the platform's file),
    /// then its all.yaml value, then USB-Helper's the same way; the first that is not empty. Null when every one is empty.
    /// </summary>
    public GuideValue? Limit(string platform, string chip, string path) => Resolve(platform, chip, path);

    /// <summary>Whether the platform or chip extends the desktop over MST (1 or 0), resolved like a limit.</summary>
    public GuideValue? Mst(string platform, string chip) => Resolve(platform, chip, "mst");

    /// <summary>
    /// The <c>exceeded_note</c> to show with a finding about a limit this guide set: its chip's note, else its all.yaml
    /// note, else USB-Helper's the same way. Null when none.
    /// </summary>
    public string? ExceededNote(string platform, string chip) =>
        Resolve(platform, chip, "exceeded_note")?.Value.Scalar as string;

    // The chip's block in the platform's file, then all.yaml.
    private GuideValue? Resolve(string platform, string chip, string field)
    {
        string[] paths = [$"platforms.{platform}.chips.{chip}.limits.{field}", $"limits.{field}"];
        for (var guide = this; guide != null; guide = guide.Fallback)
        {
            foreach (var path in paths)
            {
                if (guide.Find(path) is { Kind: not ValueKind.Null } value)
                {
                    return new GuideValue(guide, value);
                }
            }
        }
        return null;
    }
}

/// <summary>A value from a guide file, with the guide that set it (the picked guide or USB-Helper's).</summary>
public sealed record GuideValue(Guide Guide, ProfileValue Value)
{
    /// <summary>The number; null for <c>none</c>.</summary>
    public long? Number => Value.Scalar as long?;
}
