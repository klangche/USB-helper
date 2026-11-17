using System.Globalization;

namespace UsbHelper.Core.Topology;

/// <summary>
/// The user's mark on the line from a port, for an active cable, extension or extender the operating system does not
/// show (#45, #307). "+N hubs" is <see cref="OfHubs"/>: N hubs on that port's bus, and N tiers, because every hub is a
/// tier. "+N tiers" is <see cref="OfTiers"/>: only N tiers, for an extender or repeater that is not a hub.
/// </summary>
/// <param name="Hubs">The hubs the mark adds on its port's bus.</param>
/// <param name="Tiers">The tiers the mark adds to everything behind its port, its hubs included.</param>
public sealed record CableMark(int Hubs, int Tiers)
{
    /// <summary>"+N hubs": N hubs and N tiers.</summary>
    public static CableMark OfHubs(int hubs) => new(hubs, hubs);

    /// <summary>"+N tiers": N tiers, no hub.</summary>
    public static CableMark OfTiers(int tiers) => new(0, tiers);

    /// <summary>
    /// The marks the user can choose, in the menu's order (#307): +1, +2 and +3 hubs, then +1, +2 and +3 tiers. No mark
    /// (none) is not in the list.
    /// </summary>
    public static IReadOnlyList<CableMark> Choices { get; } =
        [OfHubs(1), OfHubs(2), OfHubs(3), OfTiers(1), OfTiers(2), OfTiers(3)];

    /// <summary>"+2 hubs" adds as many hubs as an active USB extension cable (#307), so its menu entry and log say so.</summary>
    public bool IsActiveExtension => this == OfHubs(2);

    /// <summary>
    /// What it adds, in English for the log and exports: "+1 hub", "+3 tiers"; a mark with hubs and more tiers than hubs,
    /// only possible in a hand-edited setup.json, says both: "+1 hub, +3 tiers".
    /// </summary>
    public override string ToString() =>
        Hubs == 0 ? Count(Tiers, "tier")
        : Hubs == Tiers ? Count(Hubs, "hub")
        : $"{Count(Hubs, "hub")}, {Count(Tiers, "tier")}";

    private static string Count(int number, string word) =>
        string.Create(CultureInfo.InvariantCulture, $"+{number} {word}{(number == 1 ? "" : "s")}");
}
