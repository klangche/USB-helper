using System.Runtime.Versioning;
using Microsoft.Win32;
using UsbHelper.Core.Detection;

namespace UsbHelper.Detection.Windows;

/// <summary>Reads the manufacturer and model from the firmware (SMBIOS) values Windows keeps in the registry. No administrator rights.</summary>
[SupportedOSPlatform("windows")]
public static class WindowsComputerIdentity
{
    public static ComputerIdentity Read()
    {
        using var bios = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
        return new ComputerIdentity(bios?.GetValue("SystemManufacturer") as string, bios?.GetValue("SystemProductName") as string);
    }
}
