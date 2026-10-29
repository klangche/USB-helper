namespace UsbHelper.Core.Database;

public enum ValueKind
{
    /// <summary>Explicit <c>null</c>: the value is unknown.</summary>
    Null,
    String,
    Integer,
    Boolean,
    List,
    Mapping,
}

/// <summary>A value from a profile file, with the file and line that defined it.</summary>
public sealed class ProfileValue
{
    private ProfileValue(ValueKind kind, object? scalar, IReadOnlyList<ProfileValue> items,
        IReadOnlyDictionary<string, ProfileValue> fields, string file, int line)
    {
        Kind = kind;
        Scalar = scalar;
        Items = items;
        Fields = fields;
        File = file;
        Line = line;
    }

    public ValueKind Kind { get; }

    /// <summary>A <see cref="string"/>, <see cref="long"/> or <see cref="bool"/>; null for lists, mappings and unknown values.</summary>
    public object? Scalar { get; }

    /// <summary>Items of a list; empty for other kinds.</summary>
    public IReadOnlyList<ProfileValue> Items { get; }

    /// <summary>Fields of a mapping, in file order; empty for other kinds.</summary>
    public IReadOnlyDictionary<string, ProfileValue> Fields { get; }

    /// <summary>Path relative to the database folder.</summary>
    public string File { get; }

    public int Line { get; }

    /// <summary>The value at a dotted path below this mapping, or null when a field on the path is missing.</summary>
    public ProfileValue? Find(string path)
    {
        var value = this;
        foreach (var key in path.Split('.'))
        {
            if (!value.Fields.TryGetValue(key, out var next))
            {
                return null;
            }
            value = next;
        }
        return value;
    }

    public static ProfileValue CreateScalar(ValueKind kind, object? scalar, string file, int line) =>
        new(kind, scalar, [], new Dictionary<string, ProfileValue>(), file, line);

    public static ProfileValue CreateList(IReadOnlyList<ProfileValue> items, string file, int line) =>
        new(ValueKind.List, null, items, new Dictionary<string, ProfileValue>(), file, line);

    public static ProfileValue CreateMapping(IReadOnlyDictionary<string, ProfileValue> fields, string file, int line) =>
        new(ValueKind.Mapping, null, [], fields, file, line);
}
