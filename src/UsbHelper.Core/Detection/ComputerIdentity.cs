namespace UsbHelper.Core.Detection;

/// <summary>What the operating system reports about the computer; each value null when it does not.</summary>
/// <param name="Chip">Processor name.</param>
/// <param name="Platform">The platform profile id that fits this computer, for example <c>windows-intel</c>.</param>
/// <param name="MachineName">The computer's network name.</param>
public sealed record ComputerIdentity(string? Manufacturer, string? Model, string? Chip = null, string? Platform = null, string? MachineName = null);
