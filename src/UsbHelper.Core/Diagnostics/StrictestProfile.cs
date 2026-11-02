using UsbHelper.Core.Database;

namespace UsbHelper.Core.Diagnostics;

/// <summary>
/// Owner decision in #68: with several profiles checked, the findings come from the strictest one only. The rule is
/// from #72: the lowest <c>max_hops</c>; on a tie the lowest <c>recommended_hops</c>, then <c>max_tiers</c>, then
/// <c>max_hubs</c>. An unknown limit counts as higher than any known one. Still tied: the first.
/// </summary>
public static class StrictestProfile
{
    private static readonly string[] Limits = ["limits.max_hops", "limits.recommended_hops", "limits.max_tiers", "limits.max_hubs"];

    /// <summary>The index of the strictest of <paramref name="profiles"/>, which must not be empty.</summary>
    public static int Of(IReadOnlyList<ResolvedProfile> profiles)
    {
        var strictest = 0;
        for (var i = 1; i < profiles.Count; i++)
        {
            if (Compare(profiles[i], profiles[strictest]) < 0)
            {
                strictest = i;
            }
        }
        return strictest;
    }

    private static int Compare(ResolvedProfile a, ResolvedProfile b) =>
        Limits.Select(field => Limit(a, field).CompareTo(Limit(b, field))).FirstOrDefault(order => order != 0);

    private static long Limit(ResolvedProfile profile, string field) => profile.Find(field)?.Scalar as long? ?? long.MaxValue;
}
