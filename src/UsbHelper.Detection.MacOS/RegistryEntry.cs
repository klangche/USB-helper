namespace UsbHelper.Detection.MacOS;

/// <summary>
/// One IORegistry entry, reduced to what the tree builder needs. Property values are long, string, bool or byte[];
/// a key that is missing means the value is unknown.
/// </summary>
/// <param name="IsUsbDevice">True when the entry conforms to IOUSBHostDevice (matched by class hierarchy, not by name).</param>
/// <param name="Error">Why the entry's properties or children could not be read completely, or null.</param>
internal sealed record RegistryEntry(
    string ClassName,
    string Name,
    bool IsUsbDevice,
    IReadOnlyDictionary<string, object> Properties,
    IReadOnlyList<RegistryEntry> Children,
    string? Error = null)
{
    public long? GetNumber(string key) => Properties.TryGetValue(key, out var value) ? value switch
    {
        long number => number,
        byte[] { Length: 4 } data => BitConverter.ToUInt32(data),
        _ => null,
    } : null;

    public string? GetString(string key) =>
        Properties.TryGetValue(key, out var value) && value is string { Length: > 0 } text ? text : null;
}
