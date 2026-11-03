using System.Runtime.InteropServices;
using System.Text;

namespace UsbHelper.App.Tests;

public class AppIconTests
{
    private static byte[] IconFile() => File.ReadAllBytes(Path.Combine(AppSource(), "Assets", "usb-helper.ico"));

    [Fact]
    public void IconHasTheUsualSizes()
    {
        using var reader = new BinaryReader(new MemoryStream(IconFile()));
        Assert.Equal(0, reader.ReadUInt16()); // reserved
        Assert.Equal(1, reader.ReadUInt16()); // 1: icon
        var count = reader.ReadUInt16();
        var widths = new List<int>();
        for (var entry = 0; entry < count; entry++)
        {
            var width = reader.ReadByte();
            widths.Add(width == 0 ? 256 : width);
            reader.ReadBytes(15); // rest of the 16-byte directory entry
        }
        Assert.Equal([16, 20, 24, 32, 40, 48, 64, 256], widths);
    }

    [Fact]
    public void EveryWindowShowsTheIcon()
    {
        // Avalonia builds the exe icon (ApplicationIcon) into the app under this name and gives it to every window.
        var defaultWindowIcon = Encoding.UTF8.GetBytes("/!__AvaloniaDefaultWindowIcon");
        var icon = IconFile();
        var app = typeof(App).Assembly;
        Assert.Contains(app.GetManifestResourceNames(), name =>
        {
            using var resource = new MemoryStream();
            app.GetManifestResourceStream(name)!.CopyTo(resource);
            var bytes = resource.ToArray().AsSpan();
            return bytes.IndexOf(defaultWindowIcon) >= 0 && bytes.IndexOf(icon) >= 0;
        });
    }

    [Fact]
    public void ExeHasTheIcon()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // Only a Windows build puts the icon into the exe.
        }
        // The referenced app's exe is copied next to the tests.
        var exe = Path.Combine(AppContext.BaseDirectory, "UsbHelper.exe");
        Assert.True(File.Exists(exe), exe);
        Assert.Equal(1u, ExtractIconEx(exe, -1, IntPtr.Zero, IntPtr.Zero, 0)); // -1: count the icons
    }

    private static string AppSource()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "UsbHelper.App");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new DirectoryNotFoundException("src/UsbHelper.App not found above the test folder.");
    }

    [DllImport("shell32.dll", EntryPoint = "ExtractIconExW", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, IntPtr large, IntPtr small, uint count);
}
