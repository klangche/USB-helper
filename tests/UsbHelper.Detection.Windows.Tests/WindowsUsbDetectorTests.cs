using UsbHelper.Core.Detection;

namespace UsbHelper.Detection.Windows.Tests;

public class WindowsUsbDetectorTests
{
    // Runs the real Windows APIs on the CI runner. Elsewhere there is nothing to call.
    [Fact]
    public void EnumerateReturnsWellFormedTreeOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var controllers = new WindowsUsbDetector().Enumerate();

        Assert.All(controllers, controller => Assert.Equal(UsbNodeKind.Controller, controller.Kind));
        var ids = new List<string>();
        Collect(controllers, ids);
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    private static void Collect(IReadOnlyList<UsbNode> nodes, List<string> ids)
    {
        foreach (var node in nodes)
        {
            ids.Add(node.Id);
            Collect(node.Children, ids);
        }
    }
}
