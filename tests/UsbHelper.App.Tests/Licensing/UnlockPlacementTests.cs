using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;

namespace UsbHelper.App.Tests.Licensing;

/// <summary>Where the unlock popup opens (#238): the top bar, and the locked branding in Settings, which keeps no code fields.</summary>
public sealed class UnlockPlacementTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";
    private static readonly XName Name = XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");

    [Fact]
    public void TheTopBarEndsWithUnlockOrWhoItIsUnlockedTo()
    {
        var area = Xaml("MainWindow.axaml").Descendants(Avalonia + "Panel").Single(element => (string?)element.Attribute(Name) == "UnlockArea");
        // The branding and the unlock area share the end of the top bar, the branding first: it is measured first, so it
        // keeps its width and the unlock text gets what is left (see BrandingHasPriorityOverTheUnlockText).
        var end = area.Parent!;
        Assert.Equal("DockPanel", end.Name.LocalName);
        Assert.Equal("Right", (string?)end.Attribute("HorizontalAlignment"));
        Assert.Null(end.ElementsAfterSelf().FirstOrDefault()); // the last child of the top bar: what is left of it
        Assert.Equal(["Branding", "UnlockArea"], end.Elements().Select(element => (string?)element.Attribute(Name)));

        var button = area.Element(Avalonia + "Button")!;
        Assert.Equal("{x:Static res:Strings.UnlockOpen}", (string?)button.Attribute("Content"));
        Assert.Equal("{Binding UnlockCommand}", (string?)button.Attribute("Command"));
        Assert.Equal("{Binding !Unlock.FeaturesUnlocked}", (string?)button.Attribute("IsVisible"));

        // The link is trimmed when narrow, shows in full on hover, and hides as the last resort below 110 px: its 8 px gap
        // and "Unlocked...", the shortest text that does not read as the locked button.
        var narrow = area.Elements().Single(element => element.Name.LocalName == "HideWhenNarrow");
        Assert.Equal("110", (string?)narrow.Attribute("MinShownWidth"));
        Assert.Equal("{Binding Unlock.FeaturesUnlocked}", (string?)narrow.Attribute("IsVisible"));
        var link = narrow.Element(Avalonia + "HyperlinkButton")!;
        var text = link.Element(Avalonia + "TextBlock")!;
        Assert.Equal("{Binding Unlock.UnlockedText}", (string?)text.Attribute("Text"));
        // Whole words: a cut "Unlocked to" must never read "Unlock...", the locked button.
        Assert.Equal("WordEllipsis", (string?)text.Attribute("TextTrimming"));
        Assert.Equal("{Binding Unlock.UnlockedText}", (string?)link.Attribute("ToolTip.Tip"));
        Assert.Equal("{Binding UnlockCommand}", (string?)link.Attribute("Command"));
        Assert.Equal("8,0,0,0", (string?)link.Attribute("Margin")); // inside HideWhenNarrow: no gap once it is hidden
    }

    // The same layout as the end of the top bar: a 96 px logo box, then the unlock text (300 px with room) in HideWhenNarrow.
    private static (Border Logo, Border Text) EndOfTopBar(double available)
    {
        var logo = new Border { Width = 96, Height = 32 };
        var text = new Border { Margin = new Thickness(8, 0, 0, 0), Child = new Border { Width = 300, Height = 16 } };
        var end = new DockPanel { Children = { logo, new Panel { Children = { new HideWhenNarrow { MinShownWidth = 110, Child = text } } } } };
        end.Measure(new Size(available, 40));
        end.Arrange(new Rect(0, 0, Math.Min(available, end.DesiredSize.Width), 40));
        return (logo, text);
    }

    [Fact]
    public void BrandingHasPriorityOverTheUnlockText()
    {
        var (logo, text) = EndOfTopBar(1000);
        Assert.Equal(96, logo.Bounds.Width);
        Assert.Equal(300, text.Bounds.Width);

        // Narrow: the logo keeps its width, the text shrinks.
        (logo, text) = EndOfTopBar(250);
        Assert.Equal(96, logo.Bounds.Width);
        Assert.Equal(250 - 96 - 8, text.Bounds.Width);

        // Below 110 px left for it, the text hides and leaves no gap; the logo stays.
        (logo, text) = EndOfTopBar(96 + 109);
        Assert.Equal(96, logo.Bounds.Width);
        Assert.Equal(0, text.Bounds.Width);
        Assert.Equal(96, ((Control)logo.Parent!).Bounds.Width);
    }

    [Fact]
    public void SettingsHasNoCodeFieldsAndItsLockedTextOpensThePopup()
    {
        var settings = Xaml("SettingsWindow.axaml");
        var text = settings.ToString();
        Assert.DoesNotContain("UnlockViewModel", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Strings.UnlockEmail", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Strings.UnlockCode", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Strings.SupportTitle", text, StringComparison.Ordinal);

        var link = settings.Descendants(Avalonia + "HyperlinkButton").Single();
        Assert.Equal("{x:Static res:Strings.UnlockNeeded}", (string?)link.Attribute("Content"));
        Assert.Equal("OpenUnlock", (string?)link.Attribute("Click"));
        Assert.Equal("{Binding !BrandingUnlocked}", (string?)link.Attribute("IsVisible"));
    }

    [Fact]
    public void ThePopupHasEmailCodeUnlockCancelAndRemoveCode()
    {
        var popup = Xaml("UnlockWindow.axaml");
        var buttons = popup.Descendants(Avalonia + "Button").ToDictionary(element => (string)element.Attribute("Content")!);
        Assert.Equal("{Binding UnlockCommand}", (string?)buttons["{x:Static res:Strings.UnlockButton}"].Attribute("Command"));
        Assert.Equal("True", (string?)buttons["{x:Static res:Strings.UnlockCancel}"].Attribute("IsCancel"));
        Assert.Equal("{Binding RemoveCommand}", (string?)buttons["{x:Static res:Strings.UnlockRemove}"].Attribute("Command"));
        Assert.Equal("{Binding IsUnlocked}", (string?)buttons["{x:Static res:Strings.UnlockRemove}"].Attribute("IsVisible"));
        var boxes = popup.Descendants(Avalonia + "TextBox").Select(element => (string?)element.Attribute("Text")).ToList();
        Assert.Equal(["{Binding Email}", "{Binding Code}"], boxes);
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
