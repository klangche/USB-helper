using System.Reflection;
using UsbHelper.App.Update;
using UsbHelper.Core.Database;

namespace UsbHelper.App;

/// <summary>
/// The hardware database built into the app (#102): every YAML file under <c>database/v&lt;N&gt;/</c> in the repository, one
/// folder per format (#334), is an embedded resource named <c>database/v&lt;N&gt;/</c> plus its path. The download has no
/// database folder. A newer database downloaded since (#290) is in <see cref="DatabaseCache"/>.
/// </summary>
public static class EmbeddedDatabase
{
    private const string Prefix = "database/v";

    /// <summary>The newest built-in format that loads (<see cref="DatabaseFormats.Choose"/>).</summary>
    /// <exception cref="DatabaseException">No built-in format loads: the errors of the newest.</exception>
    public static HardwareDatabase Load() => Load(typeof(EmbeddedDatabase).Assembly);

    /// <summary>Loads the newest database format embedded in <paramref name="assembly"/> that loads.</summary>
    /// <exception cref="DatabaseException">No format loads: the errors of the newest.</exception>
    public static HardwareDatabase Load(Assembly assembly) =>
        DatabaseFormats.Choose(Files(assembly).ToDictionary(
            format => format.Key, format => (Func<HardwareDatabase>)(() => DatabaseFormats.Read(format.Key, format.Value))), null, _ => { }).Database;

    /// <summary>The embedded files of each format folder, by format, then by path inside the folder.</summary>
    public static Dictionary<int, IReadOnlyDictionary<string, string>> Files() => Files(typeof(EmbeddedDatabase).Assembly);

    /// <summary>The files of each format folder embedded in <paramref name="assembly"/>, by format.</summary>
    public static Dictionary<int, IReadOnlyDictionary<string, string>> Files(Assembly assembly)
    {
        var formats = new Dictionary<int, Dictionary<string, string>>();
        // The build names resources after their folder with the operating system's separator.
        foreach (var (name, file) in assembly.GetManifestResourceNames().Select(name => (name, name.Replace('\\', '/'))))
        {
            var slash = file.IndexOf('/', Prefix.Length);
            if (file.StartsWith(Prefix, StringComparison.Ordinal) && slash > 0
                && int.TryParse(file[Prefix.Length..slash], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var format))
            {
                (formats.TryGetValue(format, out var files) ? files : formats[format] = [])[file[(slash + 1)..]] = Read(assembly, name);
            }
        }
        return formats.ToDictionary(format => format.Key, format => (IReadOnlyDictionary<string, string>)format.Value);
    }

    /// <summary>The database branch's commit the built-in database comes from (#290); null when the build did not record it.</summary>
    public static DatabaseVersion? Version() => Version(typeof(EmbeddedDatabase).Assembly);

    /// <summary>The commit recorded in <paramref name="assembly"/> (<c>database/.source</c>, written by release/database.sh).</summary>
    public static DatabaseVersion? Version(Assembly assembly) =>
        assembly.GetManifestResourceNames().Contains(SourceResource) ? DatabaseVersion.Parse(Read(assembly, SourceResource), builtIn: true) : null;

    private const string SourceResource = "database.source";

    private static string Read(Assembly assembly, string name)
    {
        using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
        return reader.ReadToEnd();
    }
}
