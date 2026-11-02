namespace UsbHelper.Core.Diagnostics;

/// <summary>
/// The colour of a connection line. Declared in order of precedence (owner decisions in #2 and #39): when several
/// apply, the later one wins, and over several profiles the worst one is shown.
/// </summary>
public enum ConnectionStatus
{
    /// <summary>Stable: present, no detected issue, within every limit the profile knows.</summary>
    Green,

    /// <summary>Unknown: no profile is selected, or the profile knows no limit to judge it by.</summary>
    Grey,

    /// <summary>Potentially unstable: above a recommendation (such as <c>recommended_hops</c>) but within the maximum.</summary>
    Orange,

    /// <summary>Unstable: a maximum is exceeded, or the operating system reports an error on a connection that is still present.</summary>
    Purple,

    /// <summary>A device seen earlier in this session is no longer present.</summary>
    Red,
}
