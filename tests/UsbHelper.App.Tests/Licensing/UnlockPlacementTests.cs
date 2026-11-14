using System.Xml.Linq;

namespace UsbHelper.App.Tests.Licensing;

/// <summary>
/// Where the unlock popup opens (#238): the top bar while locked, the status line while unlocked, and the locked branding in
/// Settings, which keeps no code fields.
/// </summary>
public sealed class UnlockPlacementTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";
    private static readonly XName Name = XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");

    private static XElement Named(XElement root, string name) => root.Descendants().Single(element => (string?)element.Attribute(Name) == name);

    [Fact]
    public void TheTopBarOffersUnlockOnlyWhileLockedNextToTheBranding()
    {
        var window = Xaml("MainWindow.axaml");
        var branding = Named(window, "Branding");
        var topBar = branding.Parent!;
        // The branding is unchanged (#229): the last child of the top bar, in what is left of it.
        Assert.Null(branding.ElementsAfterSelf().FirstOrDefault());
        Assert.Equal("Right", (string?)branding.Attribute("HorizontalAlignment"));

        var button = Assert.Single(branding.ElementsBeforeSelf(Avalonia + "Button"), element => (string?)element.Attribute("Content") == "{x:Static res:Strings.UnlockOpen}");
        Assert.Equal("Right", (string?)button.Attribute("DockPanel.Dock"));
        Assert.Equal("{Binding UnlockCommand}", (string?)button.Attribute("Command"));
        Assert.Equal("{Binding !Unlock.FeaturesUnlocked}", (string?)button.Attribute("IsVisible"));

        // Once unlocked the top bar shows no unlock item.
        Assert.Empty(topBar.Descendants(Avalonia + "HyperlinkButton"));
        Assert.DoesNotContain("Unlock.UnlockedText", topBar.ToString(), StringComparison.Ordinal);
        Assert.Single(topBar.Descendants(), element => element.Name.LocalName == "HideWhenNarrow"); // the company name's
    }

    [Fact]
    public void TheStatusLineAtTheBottomAlwaysShowsWhoItIsUnlockedTo()
    {
        var window = Xaml("MainWindow.axaml");
        var status = Named(window, "UnlockStatus");
        // A direct part of the window's layout, docked at the bottom before the main area, so it never gives way.
        Assert.Equal("DockPanel", status.Parent!.Name.LocalName);
        Assert.Same(window.Element(Avalonia + "DockPanel"), status.Parent);
        Assert.Equal("Bottom", (string?)status.Attribute("DockPanel.Dock"));
        Assert.Equal("Grid", status.Parent.Elements().Last().Name.LocalName);
        Assert.Equal("{Binding Unlock.FeaturesUnlocked}", (string?)status.Attribute("IsVisible"));

        var link = status.Descendants(Avalonia + "HyperlinkButton").Single();
        Assert.Equal("{Binding Unlock.UnlockedText}", (string?)link.Attribute("Content"));
        Assert.Equal("{Binding UnlockCommand}", (string?)link.Attribute("Command"));
        Assert.DoesNotContain("TextTrimming", status.ToString(), StringComparison.Ordinal);
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
