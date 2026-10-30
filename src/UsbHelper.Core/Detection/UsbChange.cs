namespace UsbHelper.Core.Detection;

public enum UsbChangeKind
{
    /// <summary>Something was connected to an empty port, or a controller appeared.</summary>
    Added,

    /// <summary>A port became empty, or a controller disappeared.</summary>
    Removed,

    /// <summary>A port's subtree or a controller changed otherwise, for example a new problem.</summary>
    Changed,

    /// <summary>
    /// What is on a port disconnected and came back before the detection read it again, so the tree is the same
    /// (owner decision in #54). <see cref="UsbTree.Apply"/> leaves the tree as it is.
    /// </summary>
    Reconnected,
}

/// <summary>One change to the USB tree. Apply it with <see cref="UsbTree.Apply"/>.</summary>
/// <param name="ParentId">Id of the hub that owns the port, or null when <paramref name="Node"/> is a controller.</param>
/// <param name="Node">
/// The port with what is now connected to it, or the controller. For a removed controller, its last state.
/// </param>
public sealed record UsbChange(UsbChangeKind Kind, string? ParentId, UsbNode Node);
