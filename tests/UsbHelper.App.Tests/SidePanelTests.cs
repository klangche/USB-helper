using System.Xml.Linq;

namespace UsbHelper.App.Tests;

/// <summary>The side panel's sections keep their headings (#265: the counts table once replaced the Disconnected heading).</summary>
public sealed class SidePanelTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";

    [Theory]
    [InlineData("{Binding Disconnected}", "{x:Static res:Strings.Disconnected}")]
    [InlineData("{Binding Findings}", "{x:Static res:Strings.Findings}")]
    public void EachListHasItsHeadingRightAboveIt(string list, string heading)
    {
        var items = Xaml("MainWindow.axaml").Descendants(Avalonia + "ItemsControl").Single(element => (string?)element.Attribute("ItemsSource") == list);

        var above = items.ElementsBeforeSelf().Last();
        Assert.Equal(Avalonia + "TextBlock", above.Name);
        Assert.Equal(heading, (string?)above.Attribute("Text"));
    }

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
}
