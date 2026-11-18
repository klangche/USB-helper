using UsbHelper.Core.Database;
using UsbHelper.Core.Diagnostics;

namespace UsbHelper.App.Update;

/// <summary>
/// The newest downloaded database (#290), in <c>database-cache/</c> next to settings.json: one folder per commit
/// (<c>&lt;sha&gt;/base.yaml</c>, ...) and a <c>current</c> file with that commit's sha and date. A new download is written
/// to a folder of its own first and becomes current only when complete, by replacing <c>current</c> in one step; older
/// folders are then deleted.
/// </summary>
public sealed class DatabaseCache(string folder)
{
    private const string CurrentFile = "current";

    /// <summary>The cache folder next to <paramref name="settingsPath"/> (settings.json).</summary>
    public static string NextTo(string settingsPath) => Path.Combine(Path.GetDirectoryName(settingsPath)!, "database-cache");

    public string Folder { get; } = folder;

    /// <summary>The cached commit, or null when there is none or <c>current</c> cannot be read.</summary>
    public DatabaseVersion? Version()
    {
        try
        {
            var current = Path.Combine(Folder, CurrentFile);
            return File.Exists(current) ? DatabaseVersion.Parse(File.ReadAllText(current), builtIn: false) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The cached database, loaded and validated as at every start; null when there is none or it no longer loads (for
    /// example a newer app reads another format), with the reason in the log.
    /// </summary>
    public (HardwareDatabase Database, DatabaseVersion Version)? Load(EventLog log)
    {
        if (Version() is not { } version)
        {
            return null;
        }
        var files = Path.Combine(Folder, version.Sha);
        try
        {
            if (!File.Exists(Path.Combine(files, "base.yaml")))
            {
                log.Add($"Downloaded database {version.ShortSha} not used: its files are missing");
                return null;
            }
            return (HardwareDatabase.Load(files), version);
        }
        catch (DatabaseException e)
        {
            log.Add($"Downloaded database {version.ShortSha} not used: {e.Errors.Count} problem(s), first: {e.Errors[0]}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Add($"Downloaded database {version.ShortSha} not used: {e.Message}");
        }
        return null;
    }

    /// <summary>Stores <paramref name="files"/> (paths checked by <see cref="DatabaseCheck.SafePath"/>) as the current cached database.</summary>
    /// <exception cref="IOException">The files could not be written; the cache is as it was.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder is not writable; the cache is as it was.</exception>
    public void Save(DatabaseVersion version, IReadOnlyDictionary<string, string> files)
    {
        if (!DatabaseVersion.IsSha(version.Sha))
        {
            throw new ArgumentException("not a commit sha", nameof(version));
        }
        Directory.CreateDirectory(Folder);
        var partial = Path.Combine(Folder, $"{version.Sha}.partial");
        if (Directory.Exists(partial))
        {
            Directory.Delete(partial, recursive: true);
        }
        foreach (var (path, text) in files)
        {
            var file = Path.Combine(partial, DatabaseCheck.SafePath(path));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text);
        }
        var target = Path.Combine(Folder, version.Sha);
        if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }
        Directory.Move(partial, target);
        var current = Path.Combine(Folder, CurrentFile);
        File.WriteAllText(current + ".tmp", (version with { BuiltIn = false }).Format());
        File.Move(current + ".tmp", current, overwrite: true);
        foreach (var old in Directory.GetDirectories(Folder).Where(each => Path.GetFileName(each) != version.Sha))
        {
            try
            {
                Directory.Delete(old, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Only space: the next download tries again.
            }
        }
    }
}
