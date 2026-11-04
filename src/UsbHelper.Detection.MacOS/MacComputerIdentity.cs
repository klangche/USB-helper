using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using UsbHelper.Core.Detection;

namespace UsbHelper.Detection.MacOS;

/// <summary>
/// Reads the model identifier (for example <c>MacBookPro18,2</c>) and the chip through <c>sysctl</c>. The manufacturer is
/// always Apple. No serial number, no hardware UUID and no administrator rights.
/// </summary>
public static class MacComputerIdentity
{
    public const string Manufacturer = "Apple Inc.";

    [SupportedOSPlatform("macos")]
    public static ComputerIdentity Read() => Build(
        ReadString("hw.model"), ReadString("machdep.cpu.brand_string"), ReadInt("hw.optional.arm64") == 1, Environment.MachineName);

    /// <summary>The identity from the raw values. Blank values are unknown (null), never guessed.</summary>
    public static ComputerIdentity Build(string? model, string? chip, bool appleSilicon, string? machineName) => new(
        Manufacturer, Clean(model), Clean(chip), appleSilicon ? "macos-apple-silicon" : "macos-intel", machineName);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [SupportedOSPlatform("macos")]
    private static string? ReadString(string name)
    {
        nuint size = 0;
        if (sysctlbyname(name, null, ref size, IntPtr.Zero, 0) != 0 || size == 0)
        {
            return null;
        }
        var buffer = new byte[size];
        if (sysctlbyname(name, buffer, ref size, IntPtr.Zero, 0) != 0)
        {
            return null;
        }
        return Encoding.UTF8.GetString(buffer, 0, (int)size).TrimEnd('\0');
    }

    // The value is 0 or missing on Intel Macs and on an Apple Silicon Mac when a process runs under Rosetta.
    [SupportedOSPlatform("macos")]
    private static int ReadInt(string name)
    {
        var buffer = new byte[4];
        nuint size = 4;
        return sysctlbyname(name, buffer, ref size, IntPtr.Zero, 0) == 0 && size == 4 ? BitConverter.ToInt32(buffer) : 0;
    }

    [DllImport("libSystem.dylib", EntryPoint = "sysctlbyname", CharSet = CharSet.Ansi)]
    private static extern int sysctlbyname(string name, byte[]? oldp, ref nuint oldlenp, IntPtr newp, nuint newlen);
}
