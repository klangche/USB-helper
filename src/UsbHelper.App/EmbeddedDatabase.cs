using System.Reflection;
using UsbHelper.App.Update;
using UsbHelper.Core.Database;

namespace UsbHelper.App;

/// <summary>
/// The hardware database built into the app (#102): every YAML file under <c>database/</c> in the repository is an
/// embedded resource named <c>database/</c> plus its path. The download has no database folder. A newer database
/// downloaded since (#290) is in <see cref="DatabaseCache"/>.
/// </summary>
public static class EmbeddedDatabase
{
    private const string Prefix = "database/";

    /// <exception cref="DatabaseException">The built-in database has one or more problems; all are listed.</exception>
    public static HardwareDatabase Load() => Load(typeof(EmbeddedDatabase).Assembly);

    /// <summary>Loads the database embedded in <paramref name="assembly"/>.</summary>
    /// <exception cref="DatabaseException">The database has one or more problems; all are listed.</exception>
    public static HardwareDatabase Load(Assembly assembly) =>
        HardwareDatabase.Load(assembly.GetManifestResourceNames()
            // The build names resources after their folder with the operating system's separator.
            .Select(name => (Name: name, File: name.Replace('\\', '/')))
            .Where(resource => resource.File.StartsWith(Prefix, StringComparison.Ordinal))
            .ToDictionary(resource => resource.File[Prefix.Length..], resource => Read(assembly, resource.Name)));

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
