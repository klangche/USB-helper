using UsbHelper.Core.Database;
using UsbHelper.Core.Diagnostics;

namespace UsbHelper.App.Update;

/// <summary>
/// The newest downloaded database of one format (#290, #334), in <c>database-cache/</c> next to settings.json: one folder
/// per commit and format (<c>&lt;sha&gt;/v3/base.yaml</c>, ...) and a <c>current-v3</c> file with that commit's sha and date.
/// Each format is cached on its own. A new download is written to a folder of its own first and becomes current only when
/// complete, by replacing <c>current-v&lt;N&gt;</c> in one step; that format's older folders are then deleted.
/// </summary>
public sealed class DatabaseCache
{
    /// <param name="folder">The cache folder, shared by all formats.</param>
    /// <param name="format">The format this cache holds; the newest this app reads when not given.</param>
    public DatabaseCache(string folder, int? format = null)
    {
        Folder = folder;
        Format = format ?? DatabaseFormats.Newest;
        CurrentFile = $"current-{DatabaseFormats.Folder(Format)}";
    }

    private string CurrentFile { get; }

    /// <summary>The cache folder next to <paramref name="settingsPath"/> (settings.json).</summary>
    public static string NextTo(string settingsPath) => Path.Combine(Path.GetDirectoryName(settingsPath)!, "database-cache");

    public string Folder { get; }

    /// <summary>The database format this cache holds.</summary>
    public int Format { get; }

    /// <summary>The cached files of <paramref name="sha"/>: <c>&lt;sha&gt;/v&lt;N&gt;</c>.</summary>
    public string FilesOf(string sha) => Path.Combine(Folder, sha, DatabaseFormats.Folder(Format));

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
        var files = FilesOf(version.Sha);
        try
        {
            if (!File.Exists(Path.Combine(files, "base.yaml")))
            {
                log.Add($"Downloaded database {version.ShortSha} not used: its files are missing");
                return null;
            }
            return (DatabaseFormats.Read(Format, Directory.EnumerateFiles(files, "*", SearchOption.AllDirectories)
                .Where(path => Path.GetExtension(path) is ".yaml" or ".yml")
                .ToDictionary(path => Path.GetRelativePath(files, path).Replace('\\', '/'), File.ReadAllText)), version);
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
        var partial = Path.Combine(Folder, $"{version.Sha}.partial-{DatabaseFormats.Folder(Format)}");
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
        var target = FilesOf(version.Sha);
        if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        Directory.Move(partial, target);
        var current = Path.Combine(Folder, CurrentFile);
        File.WriteAllText(current + ".tmp", (version with { BuiltIn = false }).Format());
        File.Move(current + ".tmp", current, overwrite: true);
        // Only this format's older folders: the other formats keep theirs. Folders that are not a commit of this layout,
        // an interrupted download's .partial folder or a cache from before #334 (files at the top, next to "current"),
        // are deleted whole.
        try
        {
            File.Delete(Path.Combine(Folder, "current"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Only space: the next download tries again.
        }
        foreach (var old in Directory.GetDirectories(Folder).Where(each => Path.GetFileName(each) != version.Sha))
        {
            try
            {
                if (!DatabaseVersion.IsSha(Path.GetFileName(old)) || File.Exists(Path.Combine(old, "base.yaml")))
                {
                    Directory.Delete(old, recursive: true);
                    continue;
                }
                var oldFiles = Path.Combine(old, DatabaseFormats.Folder(Format));
                if (Directory.Exists(oldFiles))
                {
                    Directory.Delete(oldFiles, recursive: true);
                }
                if (!Directory.EnumerateFileSystemEntries(old).Any())
                {
                    Directory.Delete(old);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Only space: the next download tries again.
            }
        }
    }
}
