using System.Reflection;
using UsbHelper.Core.Database;

namespace UsbHelper.App;

/// <summary>
/// The hardware database built into the app (#102): every YAML file under <c>database/</c> in the repository is an
/// embedded resource named <c>database/</c> plus its path. The download has no database folder.
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

    private static string Read(Assembly assembly, string name)
    {
        using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
        return reader.ReadToEnd();
    }
}
