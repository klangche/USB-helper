using System.Diagnostics;
using System.Runtime.InteropServices;
using UsbHelper.Starter.Resources;

namespace UsbHelper.Starter;

internal static partial class Program
{
    public static int Main(string[] args)
    {
        var folder = AppContext.BaseDirectory;
        if (Starter.Check(folder) is { } error)
        {
            if (OperatingSystem.IsWindows())
            {
                MessageBoxW(0, error, Strings.WindowTitle, 0x10); // MB_ICONERROR
            }
            return 1;
        }
        Process.Start(Starter.StartInfo(Starter.AppPath(folder), args, Environment.CurrentDirectory))!.Dispose();
        return 0;
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(nint owner, string text, string caption, uint type);
}
