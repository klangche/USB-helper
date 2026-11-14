using System.Xml.Linq;

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
        Assert.Equal("Right", (string?)area.Attribute("DockPanel.Dock"));
        // Next to the branding panel, which fills what is left of the top bar.
        Assert.Equal("Branding", (string?)area.ElementsAfterSelf().Single().Attribute(Name));

        var button = area.Element(Avalonia + "Button")!;
        Assert.Equal("{x:Static res:Strings.UnlockOpen}", (string?)button.Attribute("Content"));
        Assert.Equal("{Binding UnlockCommand}", (string?)button.Attribute("Command"));
        Assert.Equal("{Binding !Unlock.FeaturesUnlocked}", (string?)button.Attribute("IsVisible"));

        var link = area.Element(Avalonia + "HyperlinkButton")!;
        Assert.Equal("{Binding Unlock.UnlockedText}", (string?)link.Element(Avalonia + "TextBlock")!.Attribute("Text"));
        Assert.Equal("{Binding UnlockCommand}", (string?)link.Attribute("Command"));
        Assert.Equal("{Binding Unlock.FeaturesUnlocked}", (string?)link.Attribute("IsVisible"));
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
