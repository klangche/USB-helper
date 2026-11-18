namespace UsbHelper.App.Settings;

/// <summary>
/// custom.yaml next to settings.json (#300): the user's own guide as they wrote it. Without a path (a model without a
/// settings file) the text is kept in memory only.
/// </summary>
public sealed class CustomGuideFile(string? path)
{
    private string? text;

    /// <summary>Where the file is kept; null when it is kept in memory only.</summary>
    public string? Path { get; } = path;

    /// <summary>The saved text, or null when there is none.</summary>
    /// <exception cref="IOException">The file could not be read.</exception>
    /// <exception cref="UnauthorizedAccessException">The file is not readable.</exception>
    public string? Read() => Path == null ? text : File.Exists(Path) ? File.ReadAllText(Path) : null;

    /// <summary>Writes the text, creating the folder; written next to the file first, so a failed write keeps the old file.</summary>
    /// <exception cref="IOException">The file could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder or file is not writable.</exception>
    public void Save(string yaml)
    {
        if (Path == null)
        {
            text = yaml;
            return;
        }
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temporary = Path + ".tmp";
        File.WriteAllText(temporary, yaml);
        File.Move(temporary, Path, overwrite: true);
    }
}
