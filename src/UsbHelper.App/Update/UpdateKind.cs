namespace UsbHelper.App.Update;

/// <summary>How this app was installed, and so which release asset updates it.</summary>
public enum UpdateKind
{
    /// <summary>Unzipped from the portable zip (#123): an update opens the release page; the user downloads the zip there (#131).</summary>
    Portable,

    /// <summary>Installed by the per-user .msi (#124): updated by running the next .msi.</summary>
    Msi,
}

public static class UpdateKinds
{
    /// <summary>The .msi's install folder, below %LOCALAPPDATA% (#124; installer/Package.wxs).</summary>
    public static readonly string MsiFolder = Path.Combine("Programs", "USB-helper");

    /// <summary>
    /// The folder of USB-helper.exe: the parent of the "app" folder the app runs from (#130). Run from source, without an
    /// app folder, the app's own folder.
    /// </summary>
    public static string TopFolder(string baseDirectory)
    {
        var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDirectory));
        return string.Equals(Path.GetFileName(folder), "app", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(folder)!
            : folder;
    }

    /// <summary><see cref="UpdateKind.Msi"/> when <paramref name="topFolder"/> is the .msi's install folder, else portable.</summary>
    public static UpdateKind For(string topFolder, string localAppData) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(topFolder)),
            Path.GetFullPath(Path.Combine(localAppData, MsiFolder)), StringComparison.OrdinalIgnoreCase)
            ? UpdateKind.Msi
            : UpdateKind.Portable;

    /// <summary>The release asset's extension: usb-helper-&lt;version&gt;-windows-x64.&lt;extension&gt;.</summary>
    public static string Extension(this UpdateKind kind) => kind == UpdateKind.Msi ? "msi" : "zip";
}
