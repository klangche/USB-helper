using System.Xml.Linq;

namespace UsbHelper.App.Tests.Update;

/// <summary>The update offer is a popup window, not a bar in the main window (#131).</summary>
public class UpdatePopupTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";

    private static XElement Xaml(string file)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "UsbHelper.App", file);
            if (File.Exists(candidate))
            {
                return XElement.Load(candidate);
            }
        }
        throw new FileNotFoundException(file);
    }

    private static IEnumerable<string> Bindings(XElement root) =>
        root.DescendantsAndSelf().Attributes().Select(attribute => attribute.Value).Where(value => value.StartsWith("{Binding", StringComparison.Ordinal));

    [Fact]
    public void TheMainWindowHasNoUpdateBar()
    {
        // Only the menu item still uses the update model.
        Assert.All(Bindings(Xaml("MainWindow.axaml")).Where(binding => binding.Contains("Update", StringComparison.Ordinal)),
            binding => Assert.True(binding is "{Binding Update.CheckNow}" or "{Binding Update, Converter={x:Static ObjectConverters.IsNotNull}}", binding));
    }

    [Fact]
    public void ThePopupShowsTheMessageTheChoicesAndSkip()
    {
        var popup = Xaml("UpdateWindow.axaml");
        var bindings = Bindings(popup).ToList();
        foreach (var expected in new[]
        {
            "{Binding Message}", "{Binding StableText}", "{Binding UpdateToStable}", "{Binding BetaText}", "{Binding UpdateToBeta}",
            "{Binding CloseText}", "{Binding Skip}", "{Binding Status}", "{Binding Note}",
        })
        {
            Assert.Contains(expected, bindings);
        }
        Assert.Equal("CenterOwner", (string?)popup.Attribute("WindowStartupLocation"));
    }
}
