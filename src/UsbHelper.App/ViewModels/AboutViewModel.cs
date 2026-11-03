using System.Globalization;
using UsbHelper.App.Resources;

namespace UsbHelper.App.ViewModels;

/// <summary>The About window: name, running version, license, the icon's attribution and the repository.</summary>
public sealed class AboutViewModel
{
    public string Name => "USB-helper";

    public string Version => AppVersion.Current;

    public string VersionText => string.Format(CultureInfo.CurrentCulture, Strings.AboutVersion, Version);

    public string License => Strings.AboutLicense;

    public string IconAttribution => Strings.AboutIconAttribution;

    public Uri Repository { get; } = new("https://github.com/klangche/USB-helper");
}
