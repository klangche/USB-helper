using System.Diagnostics;

namespace UsbHelper.App.Update;

/// <summary>
/// Runs a downloaded .msi that upgrades this install in place (#124) and starts the new version when it is done (#147).
/// </summary>
public static class MsiInstall
{
    /// <summary>When 1, installer/Package.wxs starts USB-helper.exe right after the install (#147).</summary>
    public const string LaunchProperty = "LAUNCHAPPONEXIT";

    /// <summary>
    /// /passive: a progress bar and no questions, so the user sees the update happen. /norestart: if a file is still in
    /// use, Windows replaces it at the next restart instead of restarting the computer itself (/passive alone would).
    /// </summary>
    public static ProcessStartInfo StartInfo(string msi) => new("msiexec.exe")
    {
        UseShellExecute = false,
        ArgumentList = { "/i", msi, "/passive", "/norestart", $"{LaunchProperty}=1" },
    };
}
