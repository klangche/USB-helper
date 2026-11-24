using System.Globalization;
using UsbHelper.Core.Detection;

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
    /// <summary>An active cable's length in metres (#331); null for a mark without a length.</summary>
    public double? Metres { get; init; }

    /// <summary>The bus the active cable carries (#331): USB 2 or USB 3; null for a mark without a length.</summary>
    public UsbBus? Bus { get; init; }

    /// <summary>
    /// The cable's type (#372): <c>passive</c>, <c>active_copper</c>, <c>active_optical</c> or <c>usb4</c>; null for a mark
    /// without a type, read as an active cable as before. A passive cable adds no tiers, whatever its length.
    /// </summary>
    public string? Kind { get; init; }

    /// <summary>The approved cable model marked (#372), by its name in the selected philosophy; null for another cable.</summary>
    public string? Model { get; init; }

    /// <summary>The cable is a host cable (#372): on a computer port, or the cable between daisy-chained docks (D5).</summary>
    public bool Host { get; init; }

    /// <summary>The cable's length adds tiers where a measure converts it (#331): any cable with a length that is not passive.</summary>
    public bool LengthCounts => Metres != null && Kind != PassiveKind;

    /// <summary>The type of a passive cable.</summary>
    public const string PassiveKind = "passive";

    /// <summary>The cable types a mark may have, in the menu's order (#372).</summary>
    public static IReadOnlyList<string> Kinds { get; } = [PassiveKind, "active_copper", "active_optical", "usb4"];

    /// <summary>
    /// An active cable of <paramref name="metres"/> on <paramref name="bus"/> (#331): no hub, and the tiers each guide
    /// works out from its length (Crestron: the first 5 m on USB 2 or 3 m on USB 3 are free, then a tier per 5 m or 3 m).
    /// </summary>
    public static CableMark ActiveCable(double metres, UsbBus bus) => new(0, 0) { Metres = metres, Bus = bus };

    /// <summary>
    /// The tiers this cable adds when a tier comes for each <paramref name="metresPerTier"/> beyond the first: 10 m at 5 m
    /// per tier is 1, 3 m at 3 m is 0, 9 m at 3 m is 2. Zero for a mark without a length.
    /// </summary>
    public int TiersFor(double metresPerTier) =>
        Metres is { } metres && metresPerTier > 0 ? Math.Max(0, (int)Math.Ceiling(metres / metresPerTier - 1e-9) - 1) : 0;
    /// <summary>"+N hubs": N hubs and N tiers.</summary>
    public static CableMark OfHubs(int hubs) => new(hubs, hubs);

    /// <summary>"+N tiers": N tiers, no hub.</summary>
    public static CableMark OfTiers(int tiers) => new(0, tiers);

    /// <summary>
    /// The marks the user can choose, in the menu's order (owner 2026-11-17): +1, +2 (an active extender) and +3 hubs,
    /// each with as many tiers, then +1 and +2 tiers. No mark (none) is not in the list.
    /// </summary>
    public static IReadOnlyList<CableMark> Choices { get; } =
        [OfHubs(1), OfHubs(2), OfHubs(3), OfTiers(1), OfTiers(2)];

    /// <summary>"+2 hubs" adds as many hubs as an active USB extender (#307), so its menu entry and log say so.</summary>
    public bool IsActiveExtension => this == OfHubs(2);

    /// <summary>
    /// What it adds, in English for the log and exports: "+1 hub", "+3 tiers"; a mark with hubs and more tiers than hubs,
    /// only possible in a hand-edited setup.json, says both: "+1 hub, +3 tiers".
    /// </summary>
    public override string ToString() =>
        Metres is { } metres ? string.Create(CultureInfo.InvariantCulture,
            $"{(Host ? "host cable " : "")}{metres:0.#} m {(Bus == UsbBus.Usb3 ? "USB 3" : "USB 2")} {(Kind ?? "active").Replace('_', ' ')} cable{(Model == null ? "" : $" ({Model})")}")
        : Hubs == 0 ? Count(Tiers, "tier")
        : Hubs == Tiers ? Count(Hubs, "hub")
        : $"{Count(Hubs, "hub")}, {Count(Tiers, "tier")}";

    private static string Count(int number, string word) =>
        string.Create(CultureInfo.InvariantCulture, $"+{number} {word}{(number == 1 ? "" : "s")}");
}
