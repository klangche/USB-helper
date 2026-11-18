using System.Net;
using System.Text;
using System.Text.Json;
using UsbHelper.App.Update;
using UsbHelper.App.ViewModels;
using UsbHelper.Core.Database;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;

namespace UsbHelper.App.Tests.Update;

/// <summary>
/// Database updates without a new app version (#290): the newest files of this format on the database branch are
/// downloaded, checked, stored and used when newer; anything else keeps the database in use and logs one line.
/// </summary>
public sealed class DatabaseUpdateTests : IDisposable
{
    private static readonly DatabaseVersion BuiltIn = new(new string('a', 40), new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero), BuiltIn: true);
    private static readonly DatabaseVersion Newer = new(new string('b', 40), new DateTimeOffset(2026, 11, 18, 9, 12, 47, TimeSpan.Zero), BuiltIn: false);
    private static readonly DatabaseVersion Older = new(new string('c', 40), new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero), BuiltIn: false);

    // The database in use before the update: a small one with the detected platform and a picked guide.
    private static readonly HardwareDatabase Small = DatabaseFolder.Load("LENOVO", "21K5", "verified", platform: "windows-intel",
        guides: new Dictionary<string, (string, bool)> { ["desktop"] = ("Desktop", true) });

    private readonly string folder = Directory.CreateTempSubdirectory("usb-helper-database-update-").FullName;
    private readonly EventLog log = new();

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private DatabaseCache Cache => new(Path.Combine(folder, "database-cache"));

    private IEnumerable<string> Lines => log.Entries.Select(entry => entry.Text);

    // The files of the repository's database/, as on the database branch.
    private static Dictionary<string, string> RealFiles()
    {
        var database = EmbeddedDatabaseTests.RepositoryDatabase();
        return Directory.EnumerateFiles(database, "*.yaml", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(database, path).Replace('\\', '/'))
            .Where(path => !path.Split('/').Any(part => part.StartsWith('.')))
            .ToDictionary(path => path, path => File.ReadAllText(Path.Combine(database, path)));
    }

    // GitHub with one commit of v3/ on the database branch holding these files; answers 404 to anything else.
    private static FakeHttp GitHub(DatabaseVersion commit, IReadOnlyDictionary<string, string> files, params string[] moreInTree)
    {
        var commits = JsonSerializer.Serialize(new[] { new { sha = commit.Sha, commit = new { committer = new { date = commit.Date.ToString("O") } } } });
        var tree = JsonSerializer.Serialize(new
        {
            tree = new[] { new { path = "README.md", type = "blob" }, new { path = "v3", type = "tree" } }
                .Concat(files.Keys.Concat(moreInTree).Select(path => new { path = $"v3/{path}", type = "blob" }))
                .Append(new { path = "v2/base.yaml", type = "blob" }),
            truncated = false,
        });
        var answers = files.ToDictionary(file => $"{DatabaseCheck.RawUrl}{commit.Sha}/v3/{file.Key}", file => file.Value);
        answers[DatabaseCheck.CommitsUrl] = commits;
        answers[$"{DatabaseCheck.TreesUrl}{commit.Sha}?recursive=1"] = tree;
        return new FakeHttp((request, _) => Task.FromResult(answers.TryGetValue(request.RequestUri!.ToString(), out var body)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8) }
            : new HttpResponseMessage(HttpStatusCode.NotFound)));
    }

    // The window with the small database in use, its platform ticked and Desktop picked.
    private MainViewModel Window(DatabaseVersion? inUse = null)
    {
        var window = new MainViewModel(new ComputerIdentity("LENOVO", "21K5", Platform: "windows-intel"), log: log);
        window.LoadDatabase(Small, [], inUse ?? BuiltIn);
        window.Guide = Small.Guides["desktop"];
        return window;
    }

    private DatabaseUpdate Update(FakeHttp http, MainViewModel window, SettingsViewModel? settings = null) =>
        new(http.Client(), "0.9.0", Cache, window, log, TimeSpan.FromSeconds(5), settings ?? new SettingsViewModel());

    [Fact]
    public async Task ANewerDatabaseIsDownloadedCheckedStoredAndUsed()
    {
        var files = RealFiles();
        var window = Window();

        await Update(GitHub(Newer, files, "computers/.template.yaml"), window).CheckAsync();

        Assert.Equal(Newer, window.DatabaseVersion);
        Assert.Equal(HardwareDatabase.Load(files).Computers.Count, window.Computers.Count);
        Assert.Contains("Database in use: updated, 2026-11-18 (bbbbbbb)", Lines);
        // Stored for the next start, every file under the commit's folder, and marked current.
        Assert.Equal(Newer, Cache.Version());
        Assert.Equal(files["base.yaml"], File.ReadAllText(Path.Combine(Cache.Folder, Newer.Sha, "base.yaml")));
        Assert.False(File.Exists(Path.Combine(Cache.Folder, Newer.Sha, "computers", ".template.yaml")));
        // The ticks and the picked guide are kept.
        Assert.Equal(["windows-intel"], window.OperatingSystems.Where(option => option.IsChecked).Select(option => option.Profile.Id));
        Assert.Equal("desktop", window.Guide!.Id);
        Assert.NotSame(Small.Guides["desktop"], window.Guide);
        Assert.Equal(["desktop"], window.Guides.Where(option => option.IsSelected).Select(option => option.Guide!.Id));
    }

    [Fact]
    public async Task TheSameCommitIsNotDownloaded()
    {
        var http = GitHub(BuiltIn with { BuiltIn = false }, RealFiles());
        var window = Window();

        await Update(http, window).CheckAsync();

        Assert.Equal([DatabaseCheck.CommitsUrl], http.Requests.Select(uri => uri.ToString()));
        Assert.Equal(BuiltIn, window.DatabaseVersion);
        Assert.Contains("Database update check: built in, 2026-11-01 (aaaaaaa) is up to date (newest on GitHub: aaaaaaa, 2026-11-01)", Lines);
        Assert.Null(Cache.Version());
    }

    [Fact]
    public async Task AnOlderCommitIsNotDownloaded()
    {
        var http = GitHub(Older, RealFiles());
        var window = Window();

        await Update(http, window).CheckAsync();

        Assert.Single(http.Requests);
        Assert.Equal(BuiltIn, window.DatabaseVersion);
        Assert.Null(Cache.Version());
    }

    [Fact]
    public async Task AFileTheLoaderRejectsKeepsTheDatabaseInUse()
    {
        var files = RealFiles();
        files["platforms/windows.yaml"] = "name: [unclosed";
        var window = Window();

        await Update(GitHub(Newer, files), window).CheckAsync();

        Assert.Equal(BuiltIn, window.DatabaseVersion);
        Assert.Equal("desktop", window.Guide!.Id);
        Assert.Contains(Lines, line => line.StartsWith("Database update not used: ", StringComparison.Ordinal) && line.Contains("platforms/windows.yaml", StringComparison.Ordinal));
        Assert.Null(Cache.Version());
        Assert.False(Directory.Exists(Cache.Folder));
    }

    [Fact]
    public async Task OfflineKeepsTheDatabaseInUse()
    {
        var http = new FakeHttp((_, _) => throw new HttpRequestException("No such host is known."));
        var window = Window();

        await Update(http, window).CheckAsync();

        Assert.Equal(BuiltIn, window.DatabaseVersion);
        Assert.Equal("Database update check skipped: No such host is known.", Lines.Last());
    }

    [Fact]
    public async Task ARateLimitKeepsTheDatabaseInUse()
    {
        var http = new FakeHttp((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)));
        var window = Window();

        await Update(http, window).CheckAsync();

        Assert.Equal(BuiltIn, window.DatabaseVersion);
        Assert.Equal("Database update check skipped: GitHub answered 403 Forbidden", Lines.Last());
    }

    [Fact]
    public async Task AMissingFileKeepsTheDatabaseInUse()
    {
        // In the tree, but raw.githubusercontent.com answers 404.
        var window = Window();

        await Update(GitHub(Newer, RealFiles(), "guides/gone.yaml"), window).CheckAsync();

        Assert.Equal(BuiltIn, window.DatabaseVersion);
        Assert.Equal("Database update check skipped: GitHub answered 404 NotFound", Lines.Last());
        Assert.Null(Cache.Version());
    }

    [Fact]
    public async Task AnUnsafePathIsRejectedBeforeAnythingIsWritten()
    {
        var window = Window();

        await Update(GitHub(Newer, RealFiles(), "computers/../../evil.yaml"), window).CheckAsync();

        Assert.Equal(BuiltIn, window.DatabaseVersion);
        Assert.Equal("Database update check skipped: unsafe file name 'computers/../../evil.yaml'", Lines.Last());
        Assert.False(Directory.Exists(Cache.Folder));
    }

    [Fact]
    public async Task TheSettingOffAsksGitHubNothing()
    {
        var http = GitHub(Newer, RealFiles());
        var window = Window();

        await Update(http, window, new SettingsViewModel { DownloadDatabaseUpdates = false }).CheckAtStartAsync();

        Assert.Empty(http.Requests);
        Assert.Equal(BuiltIn, window.DatabaseVersion);
        Assert.Equal("Database update check is off in the settings", Lines.Last());
    }

    [Fact]
    public async Task TheSettingOnChecksAtStart()
    {
        var window = Window();

        await Update(GitHub(Newer, RealFiles()), window).CheckAtStartAsync();

        Assert.Equal(Newer, window.DatabaseVersion);
    }

    [Fact]
    public async Task TheStoredDatabaseIsUsedAtTheNextStartWhileItIsNewerThanTheBuiltInOne()
    {
        await Update(GitHub(Newer, RealFiles()), Window()).CheckAsync();

        // The next start of the same app: the stored one is newer.
        var (database, errors, version) = DatabaseUpdate.LoadAtStart(Cache, () => Small, BuiltIn, log);
        Assert.Equal(Newer, version);
        Assert.Empty(errors);
        Assert.NotSame(Small, database);

        // A newer app with a newer built-in database: that one.
        var newerBuiltIn = new DatabaseVersion(new string('d', 40), Newer.Date.AddDays(1), BuiltIn: true);
        (database, _, version) = DatabaseUpdate.LoadAtStart(Cache, () => Small, newerBuiltIn, log);
        Assert.Equal(newerBuiltIn, version);
        Assert.Same(Small, database);
    }

    [Fact]
    public void AStoredDatabaseInAnotherFormatIsIgnored()
    {
        // Stored by an older app; this one reads another format, so it no longer loads.
        var files = RealFiles();
        files["base.yaml"] = files["base.yaml"].Replace("schema_version: 3", "schema_version: 2", StringComparison.Ordinal);
        Cache.Save(Newer, files);

        var (database, errors, version) = DatabaseUpdate.LoadAtStart(Cache, () => Small, BuiltIn, log);

        Assert.Same(Small, database);
        Assert.Empty(errors);
        Assert.Equal(BuiltIn, version);
        Assert.Contains(Lines, line => line.StartsWith("Downloaded database bbbbbbb not used: 1 problem(s), first: base.yaml:", StringComparison.Ordinal));
    }

    [Fact]
    public void ANewDownloadReplacesTheStoredOne()
    {
        var files = RealFiles();
        Cache.Save(Older, files);

        Cache.Save(Newer, files);

        Assert.Equal(Newer.Sha, Cache.Version()!.Sha);
        Assert.Equal([Newer.Sha, "current"], Directory.EnumerateFileSystemEntries(Cache.Folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheBuiltInDatabaseRecordsItsCommit()
    {
        var source = File.ReadAllText(Path.Combine(EmbeddedDatabaseTests.RepositoryDatabase(), ".source"));

        var version = EmbeddedDatabase.Version();

        Assert.Equal(DatabaseVersion.Parse(source, builtIn: true), version);
        Assert.True(version!.BuiltIn);
        Assert.Null(EmbeddedDatabase.Version(typeof(DatabaseUpdateTests).Assembly));
    }

    [Fact]
    public void AboutNamesTheDatabaseInUse()
    {
        Assert.Equal("Database: built in, 2026-11-01 (aaaaaaa)", new AboutViewModel { Database = BuiltIn }.DatabaseText);
        Assert.Equal("Database: updated 2026-11-18 (bbbbbbb)", new AboutViewModel { Database = Newer }.DatabaseText);
        Assert.Equal("Database: built in", new AboutViewModel().DatabaseText);
    }
}
