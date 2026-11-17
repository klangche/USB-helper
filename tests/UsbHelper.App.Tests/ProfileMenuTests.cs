using UsbHelper.App.ViewModels;
using UsbHelper.Core.Detection;

namespace UsbHelper.App.Tests;

/// <summary>The profile menu (#286): the guides on top, one at a time; below them a tick per chip, per operating system.</summary>
public class ProfileMenuTests
{
    private static readonly ComputerIdentity Identity = new("LENOVO", "21K5", Platform: "os-p");

    private static MainViewModel Window() => new(
        DatabaseFolder.Load("LENOVO", "21K5", "verified", guides: new Dictionary<string, (string, bool)>
        {
            ["pure"] = ("Pure", true),
            ["later"] = ("Later", false),
            ["alpha"] = ("Alpha", true),
        }),
        [], Identity);

    [Fact]
    public void UsbHelperIsFirstAndPickedAtStartTheOthersByName()
    {
        var window = Window();

        Assert.Equal(["USB-Helper", "Alpha", "Later", "Pure"], window.Guides.Select(option => option.Label));
        Assert.Equal(["USB-Helper"], window.Guides.Where(option => option.IsSelected).Select(option => option.Label));
        Assert.Equal("USB-Helper", window.Guide!.Name);
        Assert.Contains("USB-Helper", window.GuideText, StringComparison.Ordinal);
    }

    [Fact]
    public void PickingAGuideUnpicksTheOthers()
    {
        var window = Window();

        window.Guides.Single(option => option.Label == "Pure").IsSelected = true;

        Assert.Equal(["Pure"], window.Guides.Where(option => option.IsSelected).Select(option => option.Label));
        Assert.Equal("Pure", window.Guide!.Name);
        Assert.Contains("Pure", window.GuideText, StringComparison.Ordinal);
    }

    [Fact]
    public void AGuideThatIsNotSelectableIsGreyedAndCannotBePicked()
    {
        var window = Window();
        var later = window.Guides.Single(option => option.Label == "Later");

        Assert.False(later.IsEnabled);
        window.Guide = later.Guide;

        Assert.Equal("USB-Helper", window.Guide!.Name);
    }

    [Fact]
    public void PickingAGuideLeavesTheOperatingSystemsTicked()
    {
        var window = Window();

        window.Guides.Single(option => option.Label == "Pure").IsSelected = true;

        Assert.True(window.OperatingSystems.Platform().IsChecked);
    }

    [Fact]
    public void ChipsAreGroupedByOperatingSystem()
    {
        var window = Window();

        var group = Assert.Single(window.PlatformGroups);
        Assert.Equal("os", group.Name);
        Assert.Equal(["p", "Computer"], group.Chips.Select(chip => chip.ChipLabel));
        Assert.Same(window.OperatingSystems.Platform(), group.Chips[0]);
    }

    [Fact]
    public void TheRealDatabaseListsTheOperatingSystemsInMenuOrder()
    {
        var window = new MainViewModel(EmbeddedDatabase.Load(), [], new ComputerIdentity("", "", Platform: "windows-intel"));

        Assert.Equal(["Windows", "macOS", "Ubuntu", "Debian", "iPhone and iPad", "Android"], window.PlatformGroups.Select(group => group.Name));
        Assert.Equal(["Intel", "AMD", "ARM"], window.PlatformGroups[0].Chips.Select(chip => chip.ChipLabel));
        Assert.Equal(["USB-Helper", "Ochno", "PureLink", "Unisynk"], window.Guides.Select(option => option.Label));
        Assert.All(window.Guides, option => Assert.True(option.IsEnabled));
    }

    // #295: clicking an operating system's name ticks all of its chips, and again unticks them all.
    [Fact]
    public void ClickingAnOperatingSystemTicksAllItsChips()
    {
        var window = Window();
        var group = Assert.Single(window.PlatformGroups);

        group.ToggleAll.Execute(null);

        Assert.All(group.Chips, chip => Assert.True(chip.IsChecked));

        group.ToggleAll.Execute(null);

        Assert.All(group.Chips, chip => Assert.False(chip.IsChecked));
    }
}
