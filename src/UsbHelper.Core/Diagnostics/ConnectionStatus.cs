namespace UsbHelper.Core.Diagnostics;

/// <summary>
/// The colour of a connection line. Declared in order of precedence (owner decision in #2):
/// when several apply, the later one wins.
/// </summary>
public enum ConnectionStatus
{
    /// <summary>Present, no detected issue, all topology limits known and satisfied.</summary>
    Green,

    /// <summary>A limit needed to judge it is unknown or unverified.</summary>
    Grey,

    /// <summary>A configured topology limit is exceeded.</summary>
    Yellow,

    /// <summary>The operating system reports an error on a connection that is still present.</summary>
    Purple,

    /// <summary>A device seen earlier in this session is no longer present.</summary>
    Red,
}
