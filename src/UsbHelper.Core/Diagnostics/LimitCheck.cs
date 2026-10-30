namespace UsbHelper.Core.Diagnostics;

public enum LimitState
{
    Within,
    Exceeded,

    /// <summary>The limit is unknown, or the actual value may be higher than counted (a hub could not be read).</summary>
    Unknown,
}

/// <summary>One topology limit compared with the detected tree.</summary>
/// <param name="Field">The profile field, for example <c>limits.max_hubs</c>.</param>
/// <param name="Actual">Hub count, or the highest tier or hop depth found.</param>
/// <param name="Limit">The limit from the computer profile or the platform profile it inherits; null when unknown.</param>
/// <param name="Verified">The limit comes from a verified or partially verified profile.</param>
/// <param name="Source">The profile file that set the limit, or null.</param>
public sealed record LimitCheck(string Field, int Actual, long? Limit, bool Verified, string? Source, LimitState State);
