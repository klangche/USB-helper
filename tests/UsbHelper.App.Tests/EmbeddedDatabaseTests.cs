using System.Globalization;
using UsbHelper.Core.Database;

namespace UsbHelper.App.Tests;

/// <summary>The database built into the app (#102).</summary>
public class EmbeddedDatabaseTests
{
    private static string RepositoryDatabase()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "database");
            if (File.Exists(Path.Combine(candidate, "base.yaml")))
            {
                return candidate;
            }
        }
        throw new DirectoryNotFoundException("database/base.yaml not found above the test output folder.");
    }

    // Every value with its path, kind, file and line, so two loads can be compared as text.
    private static IEnumerable<string> Dump(ResolvedProfile profile) =>
        Dump(profile.Root, profile.Id).Prepend(string.Join(", ", profile.Files));

    private static IEnumerable<string> Dump(ProfileValue value, string path) =>
        value.Fields.SelectMany(field => Dump(field.Value, $"{path}.{field.Key}"))
            .Concat(value.Items.SelectMany((item, i) => Dump(item, $"{path}[{i}]")))
            .Prepend(string.Create(CultureInfo.InvariantCulture, $"{path} {value.Kind} {value.Scalar} {value.File}:{value.Line}"));

    private static IEnumerable<string> Dump(IReadOnlyDictionary<string, ResolvedProfile> profiles) =>
        profiles.OrderBy(pair => pair.Key, StringComparer.Ordinal).SelectMany(pair => Dump(pair.Value));

    [Fact]
    public void EmbeddedDatabaseGivesTheSameProfilesAsTheDatabaseFolder()
    {
        var embedded = EmbeddedDatabase.Load();
        var folder = HardwareDatabase.Load(RepositoryDatabase());

        Assert.NotEmpty(embedded.Platforms);
        Assert.NotEmpty(embedded.Computers);
        Assert.Equal(Dump(folder.Platforms), Dump(embedded.Platforms));
        Assert.Equal(Dump(folder.Computers), Dump(embedded.Computers));
    }

    [Fact]
    public void MissingAndInvalidEmbeddedFilesAreReported()
    {
        // This test assembly embeds only database/platforms/broken.yaml, which is not valid YAML.
        var e = Assert.Throws<DatabaseException>(() => EmbeddedDatabase.Load(typeof(EmbeddedDatabaseTests).Assembly));

        var errors = e.Errors.Select(error => error.ToString()).ToList();
        Assert.Contains("base.yaml: File not found.", errors);
        Assert.Contains(errors, error => error.StartsWith("platforms/broken.yaml:", StringComparison.Ordinal));
    }
}
