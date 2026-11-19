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
    public void UsbHelperIsPickedAtStartAndTheGuidesAreByName()
    {
        var window = Window();

        Assert.Equal(["Alpha", "Later", "Pure", "USB-Helper"], window.Guides.Select(option => option.Label));
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
        var window = new MainViewModel(EmbeddedDatabaseTests.V3(), [], new ComputerIdentity("", "", Platform: "windows-intel"));

        Assert.Equal(["Windows", "macOS", "Ubuntu", "Debian", "iPhone and iPad", "Android"], window.PlatformGroups.Select(group => group.Name));
        Assert.Equal(["Intel", "AMD", "ARM"], window.PlatformGroups[0].Chips.Select(chip => chip.ChipLabel));
        // General-purpose guides first, then the ProAV ones, each by name (owner 2026-11-17).
        // The general-purpose guide is read by id: its name is the database's to change (#319).
        var general = window.Guides.Single(option => option.Guide?.Id == "desktop").Label;
        Assert.Equal([general, "Crestron", "Extron", "Ochno", "PureLink", "Unisynk", "USB-Helper"], window.Guides.Select(option => option.Label));
        Assert.Equal("USB-Helper", window.Guide!.Name);
        Assert.Equal([general], window.Guides.Where(option => !option.IsProAv).Select(option => option.Label));
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

    // The guide's name in the status field links to its page when its file has one.
    [Fact]
    public void TheStatusFieldLinksTheGuidesPage()
    {
        var window = new MainViewModel(EmbeddedDatabaseTests.V3(), [], new ComputerIdentity("", "", Platform: "windows-intel"));

        Assert.False(window.HasGuideLink); // USB-Helper has no sources.
        Assert.Equal("Judged by the ", window.GuideTextBefore);
        Assert.Equal(" philosophy", window.GuideTextAfter);

        window.Guides.Single(option => option.Label == "Extron").IsSelected = true;

        Assert.True(window.HasGuideLink);
        Assert.Equal("Extron", window.GuideName);
        Assert.Equal("https://www.extron.com/article/usb-c-connectivity-for-pro-av", window.GuideLink!.ToString());
    }

    // Clicking an operating system's name judges the setup once for all of its chips, not once per chip.
    [Fact]
    public void ClickingAnOperatingSystemRefreshesOnce()
    {
        var log = new UsbHelper.Core.Diagnostics.EventLog();
        var window = new MainViewModel(DatabaseFolder.Load("LENOVO", "21K5", "verified"), [], Identity, log: log);
        var group = Assert.Single(window.PlatformGroups);
        var updates = 0;
        window.StatusRows.CollectionChanged += (_, e) => updates += e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset ? 1 : 0;

        group.ToggleAll.Execute(null);

        Assert.All(group.Chips, chip => Assert.True(chip.IsChecked));
        Assert.Equal(1, updates);
    }
}
