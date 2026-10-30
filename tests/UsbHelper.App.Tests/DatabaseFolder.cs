using UsbHelper.Core.Database;

namespace UsbHelper.App.Tests;

/// <summary>A minimal valid database with one computer profile, loaded from a temporary folder that is then deleted.</summary>
internal static class DatabaseFolder
{
    public static HardwareDatabase Load(string manufacturer, string model, string status)
    {
        var folder = Directory.CreateTempSubdirectory("usb-helper-app-").FullName;
        Directory.CreateDirectory(Path.Combine(folder, "platforms"));
        Directory.CreateDirectory(Path.Combine(folder, "computers"));
        File.WriteAllText(Path.Combine(folder, "base.yaml"), "schema_version: 1\n");
        File.WriteAllText(Path.Combine(folder, "platforms", "p.yaml"), "id: p\ninherits: base\n");
        File.WriteAllText(Path.Combine(folder, "computers", "c.yaml"), $"""
            inherits: p
            manufacturer: {manufacturer}
            model: {model}
            limits:
              max_hubs: 5
              max_tiers: 7
              max_hops: 5
            displays:
              max_external: 2
            verification:
              status: {status}
            """);
        try
        {
            return HardwareDatabase.Load(folder);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
