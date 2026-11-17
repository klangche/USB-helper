namespace UsbHelper.Core.Database;

/// <summary>
/// A guide file (<c>guides/*.yaml</c>): the limits and recommendations of one guide, such as PureLink, in tiers as the
/// guide writes them. Platform and computer files hold facts; every limit beyond the USB maximum is in a guide.
/// </summary>
public sealed class Guide(string id, string name, bool selectable, ProfileValue root)
{
    /// <summary>The file's name without <c>.yaml</c>, for example <c>purelink</c>.</summary>
    public string Id { get; } = id;

    /// <summary>The guide's <c>name</c>, for example <c>PureLink</c>.</summary>
    public string Name { get; } = name;

    /// <summary>False for a guide that is listed but not filled in yet; it is not offered as a choice.</summary>
    public bool Selectable { get; } = selectable;

    /// <summary>The path relative to the database folder, for example <c>guides/purelink.yaml</c>.</summary>
    public string File => Root.File;

    /// <summary>The file's fields. Each value knows its line.</summary>
    public ProfileValue Root { get; } = root;

    /// <summary>The verification status the file writes; null when it writes none.</summary>
    public string? Status => Root.Find("verification.status")?.Scalar as string;

    /// <summary>The value at a dotted path such as <c>limits.recommended_tiers</c>, or null when the file does not set it.</summary>
    public ProfileValue? Find(string path) => Root.Find(path);
}
