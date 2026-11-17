using UsbHelper.Core.Database;

namespace UsbHelper.Core.Diagnostics;

/// <summary>
/// Owner decision in #68: with several profiles checked, the findings come from the strictest one only. The rule is
/// from #72, with the limits worked out by <see cref="DepthLimits"/> for the selected guide: the lowest maximum hop
/// depth; on a tie the lowest recommended hop depth, then the lowest <c>max_hubs</c>. An unknown limit counts as higher
/// than any known one. Still tied: the first.
/// </summary>
public static class StrictestProfile
{
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
        new[] { DepthLimits.MaxHops(profile, guide), DepthLimits.RecommendedHops(profile, guide), DepthLimits.MaxHubs(guide) }
            .Select(limit => limit.Value ?? long.MaxValue);
}
