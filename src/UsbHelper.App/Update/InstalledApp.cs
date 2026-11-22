using System.Diagnostics;

namespace UsbHelper.App.Update;

/// <summary>
/// The version of the app now on disk (#341). An update installed while this window stays open (the .msi, or a new
/// app bundle on macOS) replaces the files under it, so their version can be newer than the running one.
/// </summary>
internal static class InstalledApp
{
    /// <summary>The product version of the app's assembly on disk, without "+commit"; null when it cannot be read.</summary>
    public static string? Version() => Version(Path.Combine(AppContext.BaseDirectory, "UsbHelper.dll"));

    internal static string? Version(string path)
    {
        try
        {
            return File.Exists(path) && FileVersionInfo.GetVersionInfo(path).ProductVersion is { Length: > 0 } version
                ? version.Split('+')[0]
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
