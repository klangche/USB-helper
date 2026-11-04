namespace UsbHelper.Detection.MacOS;

/// <summary>Read-only access to the IORegistry. A fake implementation makes the tree builder testable without USB hardware.</summary>
internal interface IIoRegistryReader
{
    /// <summary>
    /// The USB host controllers (the roots of the IOUSB plane), each with every entry below it in the IOService plane:
    /// its port entries and the IOUSBHostDevice entries of the connected devices.
    /// </summary>
    IReadOnlyList<RegistryEntry> ReadControllers();
}
