using UsbHelper.Core.Topology;

namespace UsbHelper.Core.Database;

/// <summary>
/// A measure of a v4 guide (#327): a sum of path elements, such as Ochno's chain (computer_hub + hub + marked_hub).
/// </summary>
/// <param name="Id">Its key under <c>measures</c>, for example <c>chain</c>.</param>
/// <param name="Counts">The path elements it adds up (<see cref="PathElements"/>).</param>
/// <param name="Weights">How much each element counts; 1 when not given.</param>
/// <param name="ComputerHubFrom">
/// Which evidence for <c>computer_hub</c> the guide accepts: <c>detected</c>, <c>computer</c> (a computer file), <c>chip</c>
/// (a platform file). A value from anywhere else counts as unknown.
/// </param>
/// <param name="ComputerHubUnknown">The number to use when <c>computer_hub</c> is unknown; null for <c>open</c>: the result is then "at least".</param>
/// <param name="ComputerHubUnknownLine">The line of that number, for the count's origin.</param>
public sealed record GuideMeasure(
    string Id, IReadOnlyList<string> Counts, IReadOnlyDictionary<string, int> Weights, IReadOnlyList<string> ComputerHubFrom,
    int? ComputerHubUnknown, int ComputerHubUnknownLine)
{
    /// <summary>How much <paramref name="element"/> counts in this measure; 0 when it does not count.</summary>
    public int Weight(string element) => Counts.Contains(element) ? Weights.GetValueOrDefault(element, 1) : 0;
}

/// <summary>
/// One limit a v4 guide states (#327), with the exact quote it comes from. A device is judged by every rule that matches
/// it; the strictest result wins.
/// </summary>
/// <param name="Measure">The measure it limits.</param>
/// <param name="Bus"><c>usb2</c> or <c>usb3</c>; null for every bus.</param>
/// <param name="Devices">Device kinds it applies to, such as <c>camera</c> and <c>audio</c>; empty for every device.</param>
/// <param name="On">Platforms or platform/chip pairs it applies to, such as <c>windows/amd</c>; empty for all.</param>
/// <param name="Recommended">The recommendation; null when not given or <c>none</c>.</param>
/// <param name="Max">The maximum; null when not given or <c>none</c>.</param>
/// <param name="RecommendedNone">The rule says <c>recommended: none</c>: deliberately no recommendation.</param>
/// <param name="MaxNone">The rule says <c>max: none</c>: deliberately no maximum.</param>
/// <param name="Source">The id of the source in <c>sources</c>; null in a guide that is not a manufacturer's.</param>
/// <param name="Quote">The exact words of the source; null in a guide that is not a manufacturer's.</param>
public sealed record GuideRule(
    string Measure, string? Bus, IReadOnlyList<string> Devices, IReadOnlyList<string> On, long? Recommended, long? Max,
    bool RecommendedNone, bool MaxNone, string? Source, string? Quote, string File, int Line)
{
    /// <summary>What the rule applies to, without its numbers: two rules with the same selector are an error.</summary>
    public string Selector =>
        $"{Measure}|{Bus}|{string.Join(',', Devices.Order(StringComparer.Ordinal))}|{string.Join(',', On.Order(StringComparer.Ordinal))}";

    /// <summary>The rule applies to a platform and chip: <see cref="On"/> is empty or names the platform or platform/chip.</summary>
    public bool AppliesOn(string platform, string chip) => On.Count == 0 || On.Any(each => each == platform || each == $"{platform}/{chip}");
}

/// <summary>A source of a v4 guide (#327), by its id under <c>sources</c>.</summary>
public sealed record GuideSource(string Id, string? Link, string? Title, string? Author, string? Version, string? Read);
