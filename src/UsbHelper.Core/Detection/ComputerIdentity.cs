namespace UsbHelper.Core.Detection;

/// <summary>Manufacturer and model as the operating system reports them; null when it does not.</summary>
public sealed record ComputerIdentity(string? Manufacturer, string? Model);
