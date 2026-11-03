using System.IO.Compression;
using System.Text;

namespace UsbHelper.App.ViewModels;

/// <summary>The full setup export (#135): one zip with setup.json, tree.txt, screenshot.png and log.txt.</summary>
public static class SetupZip
{
    public static void Write(Stream output, string setupJson, string treeText, byte[] screenshot, string log)
    {
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        Add(zip, "setup.json", Encoding.UTF8.GetBytes(setupJson));
        Add(zip, "tree.txt", Encoding.UTF8.GetBytes(treeText));
        Add(zip, "screenshot.png", screenshot);
        Add(zip, "log.txt", Encoding.UTF8.GetBytes(log));
    }

    private static void Add(ZipArchive zip, string name, byte[] content)
    {
        using var entry = zip.CreateEntry(name).Open();
        entry.Write(content);
    }
}
