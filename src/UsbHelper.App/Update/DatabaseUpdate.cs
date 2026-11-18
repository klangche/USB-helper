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
/// </summary>
/// <param name="appVersion">This app's version, for the User-Agent GitHub asks for.</param>
/// <param name="timeout">For the whole check and download.</param>
public sealed class DatabaseUpdate(
    HttpClient http, string appVersion, DatabaseCache cache, MainViewModel window, EventLog log, TimeSpan timeout, SettingsViewModel settings)
{
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

    /// <summary>Downloads, checks, stores and uses a newer database, if there is one. Never throws. Call on the UI thread.</summary>
    public async Task CheckAsync()
    {
        using var cancel = new CancellationTokenSource(timeout);
        var current = window.DatabaseVersion;
        DatabaseVersion newest;
        Dictionary<string, string> files;
        HardwareDatabase database;
        try
        {
            newest = await DatabaseCheck.NewestAsync(http, appVersion, cancel.Token);
            if (!newest.IsNewerThan(current))
            {
                log.Add($"Database update check: {current?.LogText} is up to date (newest on GitHub: {newest.ShortSha}, {newest.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)})");
                return;
            }
            files = await DatabaseCheck.DownloadAsync(http, appVersion, newest, cancel.Token);
            database = await Task.Run(() => HardwareDatabase.Load(files), cancel.Token);
        }
        catch (DatabaseException e)
        {
            log.Add($"Database update not used: {e.Errors.Count} problem(s), first: {e.Errors[0]}");
            return;
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            log.Add($"Database update check skipped: no answer within {timeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)} s");
            return;
        }
        catch (Exception e)
        {
            // Offline, a GitHub error or rate limit, an unexpected answer: nothing else observes this task.
            log.Add($"Database update check skipped: {e.Message}");
            return;
        }
        try
        {
            cache.Save(newest, files);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // It is checked, so it is used now; the next start downloads it again.
            log.Add($"Database update {newest.ShortSha} could not be stored in {cache.Folder}: {e.Message}");
        }
        window.ReplaceDatabase(database, newest);
    }
}
