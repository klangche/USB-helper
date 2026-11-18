using System.Globalization;
using UsbHelper.App.Resources;
using UsbHelper.App.Update;

namespace UsbHelper.App.ViewModels;

/// <summary>The About window: name, running version, the database in use, license, the icon's attribution, known quirks and the repository.</summary>
public sealed class AboutViewModel
{
    public string Name => "USB-helper";

    public string Version => AppVersion.Current;

    public string VersionText => string.Format(CultureInfo.CurrentCulture, Strings.AboutVersion, Version);

    /// <summary>Which database is in use (#290); null when the build did not record it.</summary>
    public DatabaseVersion? Database { get; init; }

    /// <summary>"Database: built in, 2026-11-18 (9c92cfe)" or "Database: updated ...".</summary>
    public string DatabaseText => Database?.Text ?? Strings.AboutDatabaseUnknown;

    public string License => Strings.AboutLicense;

    public string IconAttribution => Strings.AboutIconAttribution;

    /// <summary>Hub names that look wrong but come from the hub itself, such as "USB2.1 Hub" (#225).</summary>
    public string Quirks => Strings.AboutQuirks;

    public Uri Repository { get; } = new("https://github.com/klangche/USB-helper");
}
