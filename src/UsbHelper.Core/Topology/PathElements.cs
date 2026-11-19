using System.Globalization;

namespace UsbHelper.Core.Topology;

/// <summary>
/// The parts a device's path is made of (#326). Every count the guides judge (tiers, chain, hubs) is a sum of these.
/// </summary>
public static class PathElements
{
    /// <summary>The root hub of the controller: tier 1.</summary>
    public const string RootHub = "root_hub";

    /// <summary>The hubs built into the computer that the operating system does not show, from the database.</summary>
    public const string ComputerHub = "computer_hub";

    /// <summary>An external hub the operating system reports.</summary>
    public const string Hub = "hub";

    /// <summary>A hub the user marked on a port: an active extender the operating system does not show.</summary>
    public const string MarkedHub = "marked_hub";

    /// <summary>The tiers an active cable adds, marked by the user.</summary>
    public const string CableLength = "cable_length";

    /// <summary>A USB4 router. Not measured yet (#267).</summary>
    public const string Usb4Router = "usb4_router";

    /// <summary>The hub or device itself.</summary>
    public const string Device = "device";
}

/// <summary>One part of a count with where it comes from (#326).</summary>
/// <param name="Element">One of <see cref="PathElements"/>.</param>
/// <param name="Count">How many; null when unknown, so the count is then at least the sum of the known parts.</param>
/// <param name="Origin">"detected: …", a database file and value, or "your mark on …".</param>
public sealed record CountPart(string Element, int? Count, string Origin)
{
    /// <summary>For example "computer_hub 1 platforms/macos.yaml apple-silicon" or "hub 3 detected: HUB1, HUB2, HUB3".</summary>
    public override string ToString() =>
        $"{Element} {(Count is { } count ? count.ToString(CultureInfo.InvariantCulture) : "unknown")} {Origin}";
}

/// <summary>
/// What is on the path from the computer's own port to a counted hub or device (#326): the external hubs in front of it
/// and the user's marks on that path, nearest the computer first.
/// </summary>
/// <param name="Hubs">The names of the external hubs in front of the node.</param>
/// <param name="Marks">Each mark on the path, with the name of its port.</param>
public sealed record DevicePath(IReadOnlyList<string> Hubs, IReadOnlyList<(string Port, CableMark Mark)> Marks)
{
    /// <summary>The detected and marked parts: hub, marked_hub and, for tiers, cable_length. Empty parts are left out.</summary>
    /// <param name="tiers">True for the tier count, which also counts the tiers an active cable adds.</param>
    public IEnumerable<CountPart> Parts(bool tiers)
    {
        if (Hubs.Count > 0)
        {
            yield return new CountPart(PathElements.Hub, Hubs.Count, $"detected: {string.Join(", ", Hubs)}");
        }
        foreach (var (port, mark) in Marks.Where(each => each.Mark.Hubs > 0))
        {
            yield return new CountPart(PathElements.MarkedHub, mark.Hubs, $"your mark on {port}");
        }
        if (!tiers)
        {
            yield break;
        }
        foreach (var (port, mark) in Marks.Where(each => each.Mark.Tiers > each.Mark.Hubs))
        {
            yield return new CountPart(PathElements.CableLength, mark.Tiers - mark.Hubs, $"your mark on {port}");
        }
    }
}
