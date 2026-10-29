namespace UsbHelper.Core.Detection;

/// <summary>The contract each operating system's USB detection implements. GUI and diagnostics use only this.</summary>
public interface IUsbDetector
{
    /// <summary>Reads the current USB tree: one node per host controller.</summary>
    IReadOnlyList<UsbNode> Enumerate();

    /// <summary>
    /// Watches for USB changes, starting from <paramref name="tree"/> (the result of <see cref="Enumerate"/>),
    /// until the returned object is disposed. Only the affected parts are read again; each batch of changes is
    /// passed to <paramref name="onChanges"/> on a background thread, never concurrently.
    /// </summary>
    IDisposable Watch(IReadOnlyList<UsbNode> tree, Action<IReadOnlyList<UsbChange>> onChanges);
}
