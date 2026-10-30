using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using UsbHelper.Core.Detection;

namespace UsbHelper.Detection.Windows;

/// <summary>
/// Reads the manufacturer and model from the firmware (SMBIOS) values and the processor from the values Windows keeps
/// in the registry. No administrator rights.
/// </summary>
public static class WindowsComputerIdentity
{
    [SupportedOSPlatform("windows")]
    public static ComputerIdentity Read()
    {
        using var bios = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
        using var processor = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        return new ComputerIdentity(
            bios?.GetValue("SystemManufacturer") as string,
            bios?.GetValue("SystemProductName") as string,
            (processor?.GetValue("ProcessorNameString") as string)?.Trim(),
            PlatformFor(RuntimeInformation.OSArchitecture, processor?.GetValue("VendorIdentifier") as string));
    }

    /// <summary>The Windows platform profile id; null when none fits (no guessing).</summary>
    public static string? PlatformFor(Architecture architecture, string? processorVendor) => architecture switch
    {
        Architecture.Arm64 => "windows-arm",
        Architecture.X64 when processorVendor == "GenuineIntel" => "windows-intel",
        Architecture.X64 when processorVendor == "AuthenticAMD" => "windows-amd",
        _ => null,
    };
}
