using System.Globalization;
using System.Text.RegularExpressions;
using UsbHelper.App.Resources;

namespace UsbHelper.App.Update;

/// <summary>
/// Which commit of the database branch a hardware database comes from (#290): its full sha and its commit date, and
/// whether it is the one built into the app or a newer one downloaded since.
/// </summary>
public sealed partial record DatabaseVersion(string Sha, DateTimeOffset Date, bool BuiltIn)
{
    /// <summary>The first seven characters of the sha, as GitHub shows it.</summary>
    public string ShortSha => Sha[..Math.Min(7, Sha.Length)];

    private string Day => Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>For the About window: "Database: built in, 2026-11-18 (9c92cfe)" or "Database: updated ...".</summary>
    public string Text => string.Format(CultureInfo.CurrentCulture, BuiltIn ? Strings.AboutDatabaseBuiltIn : Strings.AboutDatabaseUpdated, Day, ShortSha);

    /// <summary>The database marker (#389): "Database v4 · 2026-11-24 (abc1234)".</summary>
    public string Marker(int format) => string.Format(CultureInfo.CurrentCulture, Strings.DatabaseMarker, format, Day, ShortSha);

    /// <summary>
    /// The marker's hover (#389): the format, where it came from, the commit, and the other formats built in, one per line.
    /// </summary>
    /// <param name="others">The other formats built in, not used.</param>
    public string MarkerDetail(int format, IReadOnlyList<int> others) => string.Join('\n', new[]
    {
        string.Format(CultureInfo.CurrentCulture, Strings.DatabaseMarkerFormat, format),
        BuiltIn ? Strings.DatabaseMarkerBuiltIn : Strings.DatabaseMarkerDownloaded,
        string.Format(CultureInfo.CurrentCulture, Strings.DatabaseMarkerCommit, Sha, Day),
        others.Count == 0 ? null : string.Format(CultureInfo.CurrentCulture, Strings.DatabaseMarkerOthers, string.Join(", ", others.Select(each => $"v{each}"))),
    }.OfType<string>());

    /// <summary>For the log, in English: "built in, 2026-11-18 (9c92cfe)".</summary>
    public string LogText => $"{(BuiltIn ? "built in" : "updated")}, {Day} ({ShortSha})";

    /// <summary>True when this is a different commit with a later date than <paramref name="other"/>, or there is none.</summary>
    public bool IsNewerThan(DatabaseVersion? other) => other == null || (Sha != other.Sha && Date > other.Date);

    /// <summary>The sha and the ISO date on two lines, as <c>database/.source</c> and the cache's marker hold them.</summary>
    public string Format() => $"{Sha}\n{Date.ToString("O", CultureInfo.InvariantCulture)}\n";

    /// <summary>Reads <see cref="Format"/>'s text; null when it is not a full sha and a date.</summary>
    public static DatabaseVersion? Parse(string text, bool builtIn)
    {
        var lines = text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return lines.Length == 2 && IsSha(lines[0])
            && DateTimeOffset.TryParse(lines[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date)
            ? new DatabaseVersion(lines[0], date, builtIn)
            : null;
    }

    /// <summary>A full lowercase git sha: 40 hexadecimal characters. Only such a sha is used in a URL or a folder name.</summary>
    public static bool IsSha(string? text) => text != null && ShaPattern().IsMatch(text);

    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex ShaPattern();
}
