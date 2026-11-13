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

    /// <summary>Betas are offered next to stable releases.</summary>
    public bool OfferBetas { get; set; } = true;

    /// <summary>The checked profiles are kept for the next start.</summary>
    public bool RememberCheckedProfiles { get; set; }

    /// <summary>Platform profile ids checked when the app closed; kept only while <see cref="RememberCheckedProfiles"/> is on.</summary>
    public IReadOnlyList<string> CheckedOperatingSystems { get; set; } = [];

    /// <summary>Computer profile ids, as <see cref="CheckedOperatingSystems"/>.</summary>
    public IReadOnlyList<string> CheckedComputers { get; set; } = [];

    /// <summary>The email of the unlock code (#234); null while locked.</summary>
    public string? LicenceEmail { get; set; }

    /// <summary>The unlock code as entered; checked against <see cref="LicenceEmail"/> at every start.</summary>
    public string? LicenceCode { get; set; }
}

// Compiled serializer: camelCase names, indented so the file can be read by hand.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJson : JsonSerializerContext;
