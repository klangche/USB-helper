namespace UsbHelper.Core.Diagnostics;

public enum LimitState
{
    Within,
    Exceeded,

    /// <summary>The limit is unknown, or the actual value may be higher than counted (a hub could not be read).</summary>
    Unknown,
}

/// <summary>One topology limit compared with the detected tree.</summary>
/// <param name="Field">The check's name, for example <c>limits.max_tiers</c>.</param>
/// <param name="Actual">Hub count, or the highest tier found.</param>
/// <param name="Limit">The limit from the selected guide (<see cref="GuideLimits"/>); null when unknown or not stated.</param>
/// <param name="Verified">Every file the limit comes from is verified or partially verified.</param>
/// <param name="Source">The guide, value and file the limit comes from (<see cref="GuideLimit.Source"/>), or null.</param>
public sealed record LimitCheck(string Field, int Actual, long? Limit, bool Verified, string? Source, LimitState State);
