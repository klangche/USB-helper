using UsbHelper.Core.Diagnostics;

namespace UsbHelper.Core.Tests.Diagnostics;

/// <summary>Results grouped by outcome (#369): each system in exactly one group, by its whole set of findings.</summary>
public class OutcomeGroupsTests
{
    private static readonly Finding CamLink = new(FactKind.PredictedRisk, "CAM", "Cam Link 4K", "Cam Link 4K: tiers 5 exceeds the recommended 3.");
    private static readonly Finding Unknown = new(FactKind.Unknown, "KEYBOARD", "Keyboard", "Keyboard: tiers at least 4; the computer's own hubs are unknown.");
    private static readonly Finding Note = new(FactKind.DetectedFact, "computer", "PC", "2 external displays are active.");

    private static SystemOutcome System(string name, ConnectionStatus status, params Finding[] findings) =>
        new(name, ConnectionStatus.Red, new Dictionary<string, ConnectionStatus> { ["CAM"] = status, ["GONE"] = ConnectionStatus.Red }, findings);

    [Fact]
    public void SystemsWithTheSameFindingsFormOneGroupAndTheOrderIsBySeverity()
    {
        var groups = OutcomeGroups.Group(
        [
            System("This computer, measured", ConnectionStatus.Orange, CamLink, Note),
            System("macOS M-series", ConnectionStatus.Orange, CamLink),
            System("Windows AMD", ConnectionStatus.Green, Note),
            System("Windows Intel", ConnectionStatus.Grey, Unknown),
            System("Linux", ConnectionStatus.Orange, CamLink),
        ]);

        Assert.Equal([OutcomeKind.PotentiallyUnstable, OutcomeKind.Unknown, OutcomeKind.Good], groups.Select(group => group.Kind));
        Assert.Equal(["This computer, measured", "macOS M-series", "Linux"], groups[0].Systems);
        Assert.Equal([CamLink], groups[0].Findings);
        Assert.Equal(["Windows Intel"], groups[1].Systems);
        Assert.Equal(["Windows AMD"], groups[2].Systems);
        Assert.Empty(groups[2].Findings);
    }

    [Fact]
    public void DifferentFindingsAreDifferentGroups()
    {
        var other = new Finding(FactKind.PredictedRisk, "CAM", "Cam Link 4K", "Cam Link 4K: tiers 6 exceeds the recommended 3.");

        var groups = OutcomeGroups.Group([System("A", ConnectionStatus.Orange, CamLink), System("B", ConnectionStatus.Orange, other)]);

        Assert.Equal(2, groups.Count);
    }

    // The same groups as lines, for the exported log and the report: the title and systems, then each issue indented.
    [Fact]
    public void TheLinesSayEachIssueOnce()
    {
        var groups = OutcomeGroups.Group([System("A", ConnectionStatus.Orange, CamLink), System("B", ConnectionStatus.Orange, CamLink), System("C", ConnectionStatus.Green)]);

        Assert.Equal(["Same issue on: A, B", "  " + CamLink.Message, "Good on: C"], OutcomeGroups.Lines(groups));
    }

    // A disconnected device is the same on every system, so it does not make a system unstable.
    [Fact]
    public void ADisconnectedDeviceDoesNotDecideTheGroup() =>
        Assert.Equal(OutcomeKind.Good, OutcomeGroups.Group([System("A", ConnectionStatus.Green)]).Single().Kind);

    // A hub count over its maximum is in the overall result, not in the connections: the system is unstable, not unknown.
    [Fact]
    public void AHubLimitOverItsMaximumIsUnstable()
    {
        var hubs = new Finding(FactKind.PredictedRisk, "computer", "PC", "6 hubs are on the USB 2 bus; limits.max_hubs is 5.");

        var group = OutcomeGroups.Group([new SystemOutcome("A", ConnectionStatus.Purple, new Dictionary<string, ConnectionStatus> { ["CAM"] = ConnectionStatus.Green }, [hubs])]).Single();

        Assert.Equal(OutcomeKind.Unstable, group.Kind);
    }
}
