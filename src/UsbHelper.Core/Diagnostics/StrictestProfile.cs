using UsbHelper.Core.Database;

namespace UsbHelper.Core.Diagnostics;

/// <summary>
/// Owner decision in #68: with several profiles checked, the findings come from the strictest one only. The rule is
/// from #72, with the limits of the selected guide (<see cref="GuideLimits"/>, the lowest of USB 2 and USB 3): the lowest <c>tiers.max</c>; on a tie
/// the lowest <c>tiers.recommended</c>, then <c>hubs.max</c>. An unknown limit counts as higher than any known one.
/// Still tied: the first.
/// </summary>
public static class StrictestProfile
{
    private static readonly string[] Paths = ["tiers.max", "tiers.recommended", "hubs.max"];

    /// <summary>The index of the strictest of <paramref name="profiles"/>, which must not be empty.</summary>
    /// <param name="guide">The selected guide; null for none.</param>
    public static int Of(IReadOnlyList<ResolvedProfile> profiles, Guide? guide = null)
    {
        var strictest = 0;
        for (var i = 1; i < profiles.Count; i++)
        {
            if (Compare(profiles[i], profiles[strictest], guide) < 0)
            {
                strictest = i;
            }
        }
        return strictest;
    }

    private static int Compare(ResolvedProfile a, ResolvedProfile b, Guide? guide) =>
        Limits(a, guide).Zip(Limits(b, guide), (x, y) => x.CompareTo(y)).FirstOrDefault(order => order != 0);

    private static IEnumerable<long> Limits(ResolvedProfile profile, Guide? guide) =>
        Paths.Select(path => GuideLimits.OnBus(profile, guide, null, path).Value ?? long.MaxValue);
}
