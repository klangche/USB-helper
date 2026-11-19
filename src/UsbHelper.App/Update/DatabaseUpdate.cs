using System.Globalization;
using UsbHelper.App.ViewModels;
using UsbHelper.Core.Database;
using UsbHelper.Core.Diagnostics;

namespace UsbHelper.App.Update;

/// <summary>
/// Database updates without a new app version (#290, owner 2026-11-17): at start, in the background, the newest files
/// of this app's format on the database branch replace the database in use when they are newer, load without a problem
/// and are stored in <see cref="DatabaseCache"/> for the next start. Any failure keeps the database in use and adds one
/// line to the log; nothing is shown in a popup.
/// Each format the app reads is checked and cached on its own (#334); only the format in use replaces the database in
/// the window.
/// </summary>
/// <param name="appVersion">This app's version, for the User-Agent GitHub asks for.</param>
/// <param name="cache">The cache of the newest format; the other formats are cached in the same folder.</param>
/// <param name="timeout">For the whole check and download.</param>
/// <param name="builtIn">The built-in database's commit, to tell whether a format not in use has newer files.</param>
public sealed class DatabaseUpdate(
    HttpClient http, string appVersion, DatabaseCache cache, MainViewModel window, EventLog log, TimeSpan timeout, SettingsViewModel settings,
    DatabaseVersion? builtIn = null)
{
    /// <summary>The format to prefer at start (#334): the saved one in a beta; a release always uses the newest (null).</summary>
    public static int? PreferredFormat(bool beta, int? saved) => beta ? saved : null;

    /// <summary>
    /// The database to start with (#334): of each built-in format, the downloaded one when it is newer and still loads,
    /// else the built-in one; then the format chosen in a beta's settings, or the newest that loads
    /// (<see cref="DatabaseFormats.Choose"/>). The errors are those of a built-in database that does not load.
    /// </summary>
    /// <param name="builtIn">The built-in files of each format.</param>
    /// <param name="builtInVersion">Their commit; null when the build did not record it, so any downloaded one is newer.</param>
    /// <param name="preferred">The format chosen in a beta's settings; null for the newest.</param>
    public static (HardwareDatabase? Database, IReadOnlyList<DatabaseError> Errors, DatabaseVersion? Version, int? Format) LoadAtStart(
        string cacheFolder, IReadOnlyDictionary<int, IReadOnlyDictionary<string, string>> builtIn, DatabaseVersion? builtInVersion, EventLog log,
        int? preferred)
    {
        var versions = new Dictionary<int, DatabaseVersion?>();
        var loaders = builtIn.ToDictionary(format => format.Key, format => (Func<HardwareDatabase>)(() =>
        {
            var (database, _, version) = LoadAtStart(new DatabaseCache(cacheFolder, format.Key), () => DatabaseFormats.Read(format.Key, format.Value), builtInVersion, log);
            versions[format.Key] = version;
            return database ?? DatabaseFormats.Read(format.Key, format.Value);
        }));
        try
        {
            var (chosen, used) = DatabaseFormats.Choose(loaders, preferred, log.Add);
            return (chosen, [], versions[used], used);
        }
        catch (DatabaseException e)
        {
            return (null, e.Errors, builtInVersion, null);
        }
    }

    /// <summary>
    /// The database to start with: the downloaded one when it is newer than the built-in one and still loads, else the
    /// built-in one, with the errors of a built-in database that does not load.
    /// </summary>
    /// <param name="builtIn">Loads the built-in database.</param>
    /// <param name="builtInVersion">Its commit; null when the build did not record it, so any downloaded one is newer.</param>
    public static (HardwareDatabase? Database, IReadOnlyList<DatabaseError> Errors, DatabaseVersion? Version) LoadAtStart(
        DatabaseCache cache, Func<HardwareDatabase> builtIn, DatabaseVersion? builtInVersion, EventLog log)
    {
        if (cache.Version() is { } cached && cached.IsNewerThan(builtInVersion) && cache.Load(log) is var (database, version))
        {
            return (database, [], version);
        }
        try
        {
            return (builtIn(), [], builtInVersion);
        }
        catch (DatabaseException e)
        {
            return (null, e.Errors, builtInVersion);
        }
    }

    /// <summary>The check at start, unless "Download database updates" is off in the settings.</summary>
    public Task CheckAtStartAsync()
    {
        if (!settings.DownloadDatabaseUpdates)
        {
            log.Add("Database update check is off in the settings");
            return Task.CompletedTask;
        }
        return CheckAsync();
    }

    /// <summary>
    /// Downloads, checks and stores a newer database of each format, if there is one, and uses it when it is the format
    /// in use. Never throws. Call on the UI thread.
    /// </summary>
    public async Task CheckAsync()
    {
        foreach (var format in DatabaseFormats.Supported.Reverse())
        {
            await CheckAsync(format);
        }
    }

    private async Task CheckAsync(int format)
    {
        var inUse = format == window.DatabaseFormat;
        // The log names the format once the app reads more than one.
        var label = DatabaseFormats.Supported.Count > 1 ? $" ({DatabaseFormats.Folder(format)})" : "";
        var formatCache = new DatabaseCache(cache.Folder, format);
        using var cancel = new CancellationTokenSource(timeout);
        var current = inUse ? window.DatabaseVersion
            : formatCache.Version() is { } cached && cached.IsNewerThan(builtIn) ? cached : builtIn;
        DatabaseVersion newest;
        Dictionary<string, string> files;
        HardwareDatabase database;
        try
        {
            newest = await DatabaseCheck.NewestAsync(http, appVersion, cancel.Token, format);
            if (!newest.IsNewerThan(current))
            {
                log.Add($"Database update check{label}: {current?.LogText} is up to date (newest on GitHub: {newest.ShortSha}, {newest.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)})");
                return;
            }
            files = await DatabaseCheck.DownloadAsync(http, appVersion, newest, cancel.Token, format);
            database = await Task.Run(() => DatabaseFormats.Read(format, files), cancel.Token);
        }
        catch (DatabaseException e)
        {
            log.Add($"Database update{label} not used: {e.Errors.Count} problem(s), first: {e.Errors[0]}");
            return;
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            log.Add($"Database update check{label} skipped: no answer within {timeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)} s");
            return;
        }
        catch (Exception e)
        {
            // Offline, a GitHub error or rate limit, an unexpected answer: nothing else observes this task.
            log.Add($"Database update check{label} skipped: {e.Message}");
            return;
        }
        try
        {
            formatCache.Save(newest, files);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // It is checked, so it is used now; the next start downloads it again.
            log.Add($"Database update {newest.ShortSha} could not be stored in {formatCache.FilesOf(newest.Sha)}: {e.Message}");
        }
        if (inUse)
        {
            window.ReplaceDatabase(database, newest);
        }
    }
}
