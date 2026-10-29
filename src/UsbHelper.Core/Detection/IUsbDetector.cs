namespace UsbHelper.Core.Detection;

/// <summary>
/// The contract each operating system's USB detection implements. GUI and diagnostics use only this.
/// Change events are added with live monitoring (step 4).
/// </summary>
public interface IUsbDetector
{
    /// <summary>Reads the current USB tree: one node per host controller.</summary>
    IReadOnlyList<UsbNode> Enumerate();
}
