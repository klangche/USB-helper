namespace UsbHelper.Core.Database;

/// <summary>
/// The database formats this app reads (#334), one folder each on the database branch (<c>v3/</c>, <c>v4/</c>, ...).
/// Each format has its own reader, and every reader gives the same <see cref="HardwareDatabase"/>. The app uses the
/// newest format that is present and loads. A folder of a format the app does not know, such as a later <c>v5/</c>,
/// is ignored, so publishing a new format never breaks installed apps. Adding a format means adding a reader here.
/// </summary>
public static class DatabaseFormats
{
    // One reader per format. release/database.sh reads the supported formats from the "[N] =" lines.
    private static readonly Dictionary<int, Func<IReadOnlyDictionary<string, string>, HardwareDatabase>> Readers = new()
    {
        [3] = HardwareDatabase.Load,
    };

    /// <summary>The formats this app reads, oldest first.</summary>
    public static IReadOnlyList<int> Supported { get; } = [.. Readers.Keys.Order()];

    /// <summary>The newest format this app reads.</summary>
    public static int Newest => Supported[^1];

    /// <summary>The folder of a format on the database branch and in the build: <c>v3</c>.</summary>
    public static string Folder(int format) => $"v{format}";

    /// <summary>Reads <paramref name="files"/> (the contents of one format's folder) with that format's reader.</summary>
    /// <exception cref="DatabaseException">The files have one or more problems; all are listed.</exception>
    public static HardwareDatabase Read(int format, IReadOnlyDictionary<string, string> files) => Readers[format](files);

    /// <summary>
    /// The database to use: <paramref name="preferred"/> when it is given, present and loads, else the newest present
    /// format that loads. Formats not in <paramref name="supported"/> are ignored. Each format that is tried and does
    /// not load, and the format in use with why it is not the newest, is a line in <paramref name="log"/>.
    /// </summary>
    /// <param name="present">A loader per format folder that exists; each throws <see cref="DatabaseException"/> when its files have problems.</param>
    /// <param name="preferred">The format chosen in a beta's settings; null for the newest.</param>
    /// <param name="supported">The formats the app reads; <see cref="Supported"/> by default.</param>
    /// <exception cref="DatabaseException">No format loads: the errors of the first one tried.</exception>
    public static (HardwareDatabase Database, int Format) Choose(
        IReadOnlyDictionary<int, Func<HardwareDatabase>> present, int? preferred, Action<string> log, IReadOnlyList<int>? supported = null)
    {
        supported ??= Supported;
        var formats = present.Keys.Where(supported.Contains).OrderDescending().ToList();
        if (formats.Count == 0)
        {
            throw new DatabaseException([new DatabaseError("", 0, $"No database folder of a format this app reads ({string.Join(", ", supported.Select(Folder))}).")]);
        }
        var newest = formats[0];
        if (preferred is { } chosen && formats.Remove(chosen))
        {
            formats.Insert(0, chosen);
        }
        DatabaseException? first = null;
        foreach (var format in formats)
        {
            try
            {
                var database = present[format]();
                var why = format == newest ? ""
                    : format == preferred ? ", chosen in the settings"
                    : $", because {Folder(newest)} does not load";
                log($"Database format {Folder(format)} in use{why}");
                return (database, format);
            }
            catch (DatabaseException e)
            {
                first ??= e;
                log($"Database format {Folder(format)} not used: {e.Errors.Count} problem(s), first: {e.Errors[0]}");
            }
        }
        throw first!;
    }
}
