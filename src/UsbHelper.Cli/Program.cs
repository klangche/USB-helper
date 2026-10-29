using UsbHelper.Core.Detection;
using UsbHelper.Detection.Windows;

// Prints the detected USB tree, for checking detection against USBView or Device Manager.
// With --watch it then prints each change until Enter is pressed.
if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("USB detection is only implemented for Windows.");
    return 1;
}

var detector = new WindowsUsbDetector();
var tree = detector.Enumerate();
Print(tree, 0);

if (args.Contains("--watch"))
{
    Console.WriteLine();
    Console.WriteLine("Watching for changes. Plug or unplug a device; press Enter to stop.");
    using var watch = detector.Watch(tree, changes =>
    {
        foreach (var change in changes)
        {
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} {change.Kind} on {change.ParentId ?? "the controller list"}:");
            Print([change.Node], 1);
        }
    });
    Console.ReadLine();
}
return 0;

static void Print(IReadOnlyList<UsbNode> nodes, int depth)
{
    foreach (var node in nodes)
    {
        Console.WriteLine(new string(' ', depth * 2) + Describe(node));
        Print(node.Children, depth + 1);
    }
}

static string Describe(UsbNode node)
{
    var text = node.Kind == UsbNodeKind.Port
        ? node.Name + (node.Children.Count == 0 ? ": empty" : "")
        : $"[{node.Kind}] {node.Name}" + (node.VendorId is { } vendor ? $" {vendor:X4}:{node.ProductId:X4}" : "") + $"  ({node.Id})";
    return node.Problem == null ? text : $"{text}  PROBLEM: {node.Problem}";
}
