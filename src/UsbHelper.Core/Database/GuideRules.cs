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
    /// <summary>
    /// Metres of active cable per tier, by bus (<c>usb2</c>, <c>usb3</c>), from <c>cable_length</c> (#331); empty when the
    /// measure does not convert a cable's length.
    /// </summary>
    public IReadOnlyDictionary<string, double> CableMetresPerTier { get; init; } = new Dictionary<string, double>();

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
/// <param name="Link">The first web link among its entries, for the guide's page; null when it has none.</param>
public sealed record GuideSource(string Id, string? Link, string? Title, string? Author, string? Version, string? Read)
{
    /// <summary>Its PDFs and web links, in the order listed (#333); at least one when the source sets <c>files</c>.</summary>
    public IReadOnlyList<SourceFile> Files { get; init; } = [];

    /// <summary>Its quotes, each pointing at one of <see cref="Files"/> and a page (#333).</summary>
    public IReadOnlyList<SourceQuote> Quotes { get; init; } = [];
}

/// <summary>
/// One PDF or web link of a source (#333). A PDF may have no URL (an unpublished source): then only its fingerprint is
/// known, and a user who has the file opens it with "Open local file".
/// </summary>
/// <param name="Name">What is shown: the entry's <c>name</c>, or, for a source's only entry, the URL's decoded file name or host.</param>
/// <param name="Url">The PDF's or page's address; null for a PDF without a public download.</param>
/// <param name="IsPdf">A <c>pdf</c> entry, opened in the built-in viewer; else a <c>link</c>, opened in the browser.</param>
/// <param name="Sha256">The PDF's SHA-256 in lowercase hex, when known.</param>
/// <param name="Size">The PDF's size in bytes, when known.</param>
public sealed record SourceFile(string Name, Uri? Url, bool IsPdf, string? Sha256, long? Size);

/// <summary>A quote from a source (#333), with the entry it is in and its page there.</summary>
public sealed record SourceQuote(string Text, string? File, int? Page);

/// <summary>
/// A note on cable lengths in a philosophy (#372), shown on a marked cable's hover and in the report; it never changes a
/// status.
/// </summary>
/// <param name="Type">The cable type it is about, as <see cref="Topology.CableMark.Kinds"/> names them.</param>
/// <param name="Bus"><c>usb2</c> or <c>usb3</c>; null for both.</param>
/// <param name="Text">The note, as the philosophy words it: "passive USB 3.2 Gen 1 about 2 m (indicative)".</param>
public sealed record CableNote(string Type, string? Bus, string Text, string? Source, string? Quote);

/// <summary>A cable model a philosophy approves (#372), for the notes and for marking a cable as that model.</summary>
/// <param name="Name">Its name, as the user picks it: "PureInstall PI6600 (21002)".</param>
/// <param name="Length">Its rated length in metres.</param>
/// <param name="Speed">The speed it is rated for, as its maker states it; null when not stated.</param>
/// <param name="Hubs">The hubs it adds, when its maker states them; null for the default of its type.</param>
/// <param name="Tiers">The tiers it adds, when its maker states them; null for the default of its type.</param>
/// <param name="HostAllowed">False when its maker says it does not work as the host cable.</param>
/// <param name="Note">More from its maker, such as "240 W, lockable"; null for none.</param>
public sealed record CableModel(string Name, string Type, double Length, string? Speed, int? Hubs, int? Tiers, bool HostAllowed, string? Note, string? Source)
{
    /// <summary>The bus it carries, when its maker states it; marking it sets the mark's bus. Null: the bus picked.</summary>
    public Detection.UsbBus? Bus { get; init; }
}
