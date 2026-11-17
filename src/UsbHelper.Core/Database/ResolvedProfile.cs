namespace UsbHelper.Core.Database;

/// <summary>
/// One operating system and chip (a platform profile), or one computer on top of its operating system and chip, with
/// everything it inherits applied: <c>base.yaml</c>, then the platform file with the chip's fields, then the computer file.
/// </summary>
public sealed class ResolvedProfile(string id, IReadOnlyList<string> files, ProfileValue root, IReadOnlyDictionary<string, string?> statuses)
{
    /// <summary>A platform profile's id is the platform and the chip, for example <c>macos-apple-silicon</c>; a computer's is computed.</summary>
    public string Id { get; } = id;

    /// <summary>The platform file's name without <c>.yaml</c>, for example <c>macos</c>.</summary>
    public string Platform { get; init; } = "";

    /// <summary>The platform's <c>name</c>, for example <c>macOS</c>.</summary>
    public string PlatformName { get; init; } = "";

    /// <summary>The chip's key in the platform file, for example <c>apple-silicon</c>.</summary>
    public string Chip { get; init; } = "";

    /// <summary>The chip's <c>name</c>, for example <c>M-series</c>.</summary>
    public string ChipName { get; init; } = "";

    /// <summary>The files that make up this profile, base first. Paths are relative to the database folder.</summary>
    public IReadOnlyList<string> Files { get; } = files;

    /// <summary>
    /// The resolved fields, without the platform's and the chip's <c>name</c>, <c>chips</c>, and a computer's
    /// <c>platform</c> and <c>chip_family</c>. Each value knows the file and line that set it.
    /// </summary>
    public ProfileValue Root { get; } = root;

    /// <summary>
    /// The value at a dotted path such as <c>built_in_hub_levels</c>, or null when no file in the chain defines it
    /// (or a field on the path is explicitly null).
    /// A defined value of kind <see cref="ValueKind.Null"/> means the value is explicitly unknown.
    /// </summary>
    public ProfileValue? Find(string path) => Root.Find(path);

    /// <summary>
    /// The verification status that <paramref name="file"/> itself writes, not this profile's: a value inherited
    /// from a platform keeps the platform's status. Null when the file is not in the chain or writes none.
    /// </summary>
    public string? StatusOf(string file) => statuses.GetValueOrDefault(file);
}
