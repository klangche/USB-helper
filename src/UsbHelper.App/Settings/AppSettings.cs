using System.Text.Json;
using System.Text.Json.Serialization;

namespace UsbHelper.App.Settings;

/// <summary>
/// What settings.json holds (#126). A setting missing from the file keeps its default. The setters are public rather than
/// init-only: the compiled serializer gives a missing init-only property its type's default (false), not the one here.
/// </summary>
public sealed record AppSettings
{
    public bool CheckForUpdatesAtStart { get; set; } = true;

    /// <summary>Newer database files are downloaded from the database branch at start (#290).</summary>
    public bool DownloadDatabaseUpdates { get; set; } = true;

    /// <summary>Betas are offered next to stable releases.</summary>
    public bool OfferBetas { get; set; } = true;

    /// <summary>
    /// The database format a beta uses (#334); null for the newest. Release builds ignore it and always use the newest.
    /// </summary>
    public int? DatabaseFormat { get; set; }

    /// <summary>The checked profiles are kept for the next start.</summary>
    public bool RememberCheckedProfiles { get; set; }

    /// <summary>Platform profile ids checked when the app closed; kept only while <see cref="RememberCheckedProfiles"/> is on.</summary>
    public IReadOnlyList<string> CheckedOperatingSystems { get; set; } = [];

    /// <summary>
    /// Computer profile ids from versions before #286; no longer written or read: computers are never checked at start.
    /// </summary>
    public IReadOnlyList<string> CheckedComputers { get; set; } = [];

    /// <summary>The picked guide's id (#286), as <see cref="CheckedOperatingSystems"/>; null for USB-Helper's.</summary>
    public string? CheckedGuide { get; set; }

    /// <summary>Shown on reports (#228); trimmed, and empty means none.</summary>
    public string CompanyName { get; set; } = "";

    /// <summary>The email of the unlock code (#234); null while locked.</summary>
    public string? LicenceEmail { get; set; }

    /// <summary>The unlock code as entered; checked against <see cref="LicenceEmail"/> at every start.</summary>
    public string? LicenceCode { get; set; }
}

// Compiled serializer: camelCase names, indented so the file can be read by hand.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJson : JsonSerializerContext;
