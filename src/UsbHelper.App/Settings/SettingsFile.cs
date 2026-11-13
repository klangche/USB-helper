using System.Text.Json;
using UsbHelper.Core.Diagnostics;

namespace UsbHelper.App.Settings;

/// <summary>
/// settings.json in the user's roaming app data folder (#121, #126): the same for the .msi and the portable zip, and
/// outside the app's folder, so it survives updates.
/// </summary>
public sealed class SettingsFile(string path)
{
    /// <summary><c>%APPDATA%\USB-helper\settings.json</c> on Windows.</summary>
    public static SettingsFile Default { get; } =
        new(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "USB-helper", "settings.json"));

    public string Path { get; } = path;

    /// <summary>
    /// The saved settings. No file: the defaults. A file that cannot be read: the defaults, and the reason in the log; the
    /// file is left as it is until a setting is changed.
    /// </summary>
    public AppSettings Load(EventLog log)
    {
        if (!File.Exists(Path))
        {
            return new AppSettings();
        }
        try
        {
            var settings = JsonSerializer.Deserialize(File.ReadAllText(Path), SettingsJson.Default.AppSettings)
                ?? throw new JsonException("the file holds no settings");
            // "null" for a list or the company name in the file means none.
            return settings with
            {
                CheckedOperatingSystems = settings.CheckedOperatingSystems ?? [],
                CheckedComputers = settings.CheckedComputers ?? [],
                CompanyName = settings.CompanyName ?? "",
            };
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            log.Add($"Settings file {Path} could not be read ({e.Message}); using the defaults");
            return new AppSettings();
        }
    }

    /// <summary>Writes the settings, creating the folder. Written next to the file first, so a failed write never leaves half a file.</summary>
    /// <exception cref="IOException">The file could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder or file is not writable.</exception>
    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temporary = Path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, SettingsJson.Default.AppSettings));
        File.Move(temporary, Path, overwrite: true);
    }
}
