using UsbHelper.Core.Detection;
using static UsbHelper.Core.Tests.Detection.TestTree;

namespace UsbHelper.Core.Tests.Detection;

public class UsbTreeTests
{
    private static readonly IReadOnlyList<UsbNode> Tree =
    [
        Controller("C1", Hub("ROOT1", Device("MOUSE"), Hub("HUB", Device("KEYBOARD"), null))),
        Controller("C2", Hub("ROOT2", (UsbNode?)null)),
    ];

    [Fact]
    public void PathToListsNodesFromController()
    {
        Assert.Equal(
            ["C1", "ROOT1", "ROOT1|port2", "HUB", "HUB|port1", "KEYBOARD"],
            UsbTree.PathTo(Tree, "KEYBOARD").Select(node => node.Id));
        Assert.Empty(UsbTree.PathTo(Tree, "MISSING"));
        Assert.Null(UsbTree.Find(Tree, "MISSING"));
    }

    [Fact]
    public void ParentHubIsTheHubOwningThePort()
    {
        Assert.Equal("HUB", UsbTree.ParentHub(Tree, "KEYBOARD")!.Id);
        Assert.Equal("ROOT1", UsbTree.ParentHub(Tree, "HUB")!.Id);
        Assert.Null(UsbTree.ParentHub(Tree, "ROOT1"));
        Assert.Null(UsbTree.ParentHub(Tree, "C1"));
    }

    [Fact]
    public void ApplyingPortChangeKeepsUnaffectedNodes()
    {
        var change = new UsbChange(UsbChangeKind.Added, "HUB", Port("HUB", 2, Device("STICK")));

        var updated = UsbTree.Apply(Tree, [change]);

        Assert.Equal("STICK", UsbTree.Find(updated, "STICK")!.Id);
        Assert.Same(Tree[1], updated[1]);
        Assert.Same(UsbTree.Find(Tree, "MOUSE"), UsbTree.Find(updated, "MOUSE"));
        Assert.Same(UsbTree.Find(Tree, "HUB|port1"), UsbTree.Find(updated, "HUB|port1"));
    }

    [Fact]
    public void ApplyingControllerChangesAddsAndRemoves()
    {
        var added = Controller("C3", Hub("ROOT3"));

        var updated = UsbTree.Apply(Tree, [new UsbChange(UsbChangeKind.Removed, null, Tree[1]), new UsbChange(UsbChangeKind.Added, null, added)]);

        Assert.Equal(["C1", "C3"], updated.Select(node => node.Id));
    }
}
