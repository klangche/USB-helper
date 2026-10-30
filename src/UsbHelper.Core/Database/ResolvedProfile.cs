namespace UsbHelper.Core.Database;

/// <summary>A platform or computer profile with everything it inherits applied.</summary>
public sealed class ResolvedProfile(string id, IReadOnlyList<string> files, ProfileValue root, IReadOnlyDictionary<string, string?> statuses)
{
    public string Id { get; } = id;

    /// <summary>The files that make up this profile, base first. Paths are relative to the database folder.</summary>
    public IReadOnlyList<string> Files { get; } = files;

    /// <summary>The resolved fields, without <c>id</c> and <c>inherits</c>. Each value knows the file and line that set it.</summary>
    public ProfileValue Root { get; } = root;

    /// <summary>
    /// The value at a dotted path such as <c>limits.max_hubs</c>, or null when no file in the chain defines it
    /// (or a field on the path is explicitly null).
    /// A defined value of kind <see cref="ValueKind.Null"/> means the value is explicitly unknown.
    /// </summary>
    public ProfileValue? Find(string path) => Root.Find(path);

    /// <summary>
    /// The verification status of <paramref name="file"/> itself (what it sets or inherits), not of this profile:
    /// a value inherited from a platform keeps the platform's status. Null when the file is not in the chain or has none.
    /// </summary>
    public string? StatusOf(string file) => statuses.GetValueOrDefault(file);
}
