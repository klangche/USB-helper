using UsbHelper.Core.Detection;
using static UsbHelper.Core.Tests.Detection.TestTree;

namespace UsbHelper.Core.Tests.Detection;

public class UsbTreeDiffTests
{
    private static IReadOnlyList<UsbNode> Machine(UsbNode? port1, UsbNode? port2) => [Controller("C1", Hub("ROOT", port1, port2))];

    private static UsbChange Single(IReadOnlyList<UsbNode> before, IReadOnlyList<UsbNode> after) =>
        Assert.Single(UsbTreeDiff.Compare(before, after));

    [Fact]
    public void SameTreeHasNoChanges()
    {
        Assert.Empty(UsbTreeDiff.Compare(Machine(Device("MOUSE"), null), Machine(Device("MOUSE"), null)));
    }

    [Fact]
    public void DeviceOnEmptyPortIsAdded()
    {
        var change = Single(Machine(null, null), Machine(null, Device("MOUSE")));

        Assert.Equal(UsbChangeKind.Added, change.Kind);
        Assert.Equal("ROOT", change.ParentId);
        Assert.Equal("ROOT|port2", change.Node.Id);
        Assert.Equal("MOUSE", change.Node.Children[0].Id);
    }

    [Fact]
    public void UnpluggedDeviceIsRemoved()
    {
        var change = Single(Machine(Device("MOUSE"), null), Machine(null, null));

        Assert.Equal(UsbChangeKind.Removed, change.Kind);
        Assert.Equal("ROOT|port1", change.Node.Id);
        Assert.Empty(change.Node.Children);
    }

    [Fact]
    public void OtherDeviceOnSamePortIsChanged()
    {
        Assert.Equal(UsbChangeKind.Changed, Single(Machine(Device("MOUSE"), null), Machine(Device("STICK"), null)).Kind);
    }

    [Fact]
    public void NewProblemIsChanged()
    {
        var change = Single(Machine(Device("MOUSE"), null), Machine(Device("MOUSE", "code 43"), null));

        Assert.Equal(UsbChangeKind.Changed, change.Kind);
        Assert.Equal("code 43", change.Node.Children[0].Problem);
    }

    [Fact]
    public void ChangeBehindHubIsReportedOnThatHubsPort()
    {
        var change = Single(Machine(Hub("HUB", null, null), null), Machine(Hub("HUB", null, Device("KEYBOARD")), null));

        Assert.Equal(UsbChangeKind.Added, change.Kind);
        Assert.Equal("HUB", change.ParentId);
        Assert.Equal("HUB|port2", change.Node.Id);
    }

    [Fact]
    public void UnpluggedHubIsOneRemoval()
    {
        var change = Single(Machine(Hub("HUB", Device("KEYBOARD"), Device("MOUSE")), null), Machine(null, null));

        Assert.Equal(UsbChangeKind.Removed, change.Kind);
        Assert.Equal("ROOT|port1", change.Node.Id);
    }

    [Fact]
    public void ControllersAreAddedAndRemoved()
    {
        IReadOnlyList<UsbNode> before = [Controller("C1", Hub("ROOT1")), Controller("DOCK", Hub("ROOT2"))];
        IReadOnlyList<UsbNode> after = [Controller("C1", Hub("ROOT1")), Controller("C3", Hub("ROOT3"))];

        var changes = UsbTreeDiff.Compare(before, after);

        Assert.Equal(
            [(UsbChangeKind.Removed, "DOCK"), (UsbChangeKind.Added, "C3")],
            changes.Select(change => (change.Kind, change.Node.Id)));
        Assert.All(changes, change => Assert.Null(change.ParentId));
    }

    [Fact]
    public void RootHubProblemChangesTheController()
    {
        IReadOnlyList<UsbNode> after = [Controller("C1", Hub("ROOT") with { Problem = "Could not read the hub." })];

        var change = Single([Controller("C1", Hub("ROOT"))], after);

        Assert.Equal(UsbChangeKind.Changed, change.Kind);
        Assert.Null(change.ParentId);
    }

    public static TheoryData<int> Cases => [0, 1, 2, 3, 4];

    private static readonly IReadOnlyList<UsbNode>[] Trees =
    [
        Machine(null, null),
        Machine(Device("MOUSE"), Hub("HUB", Device("KEYBOARD"), null)),
        Machine(Device("MOUSE", "code 43"), Hub("HUB", null, Device("STICK"))),
        Machine(Hub("HUB", Device("KEYBOARD"), null), Device("MOUSE")),
        [Controller("C1", Hub("ROOT", null, null)), Controller("DOCK", Hub("DOCKROOT", Device("NIC")))],
    ];

    [Theory]
    [MemberData(nameof(Cases))]
    public void ApplyingTheChangesGivesTheNewTree(int target)
    {
        foreach (var before in Trees)
        {
            var after = Trees[target];

            var result = UsbTree.Apply(before, UsbTreeDiff.Compare(before, after));

            Assert.Equal(after.Count, result.Count);
            Assert.All(after.Zip(result), pair => Assert.True(UsbTreeDiff.SameTree(pair.First, pair.Second)));
        }
    }
}
