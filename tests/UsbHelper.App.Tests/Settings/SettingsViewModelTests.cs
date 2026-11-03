using UsbHelper.App.Settings;
using UsbHelper.App.ViewModels;
using UsbHelper.Core.Diagnostics;

namespace UsbHelper.App.Tests.Settings;

/// <summary>The settings window (#126): every change is written at once; a failed write is logged, not thrown.</summary>
public sealed class SettingsViewModelTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("usbhelper-settings-vm-tests-").FullName;
    private readonly EventLog log = new();

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private string FilePath => Path.Combine(folder, "USB-helper", "settings.json");

    [Fact]
    public void EachChangeIsWrittenAtOnce()
    {
        var file = new SettingsFile(FilePath);
        var settings = SettingsViewModel.Load(file, log);

        settings.CheckForUpdatesAtStart = false;
        Assert.False(file.Load(log).CheckForUpdatesAtStart);

        settings.OfferBetas = false;
        Assert.False(file.Load(log).OfferBetas);

        settings.RememberCheckedProfiles = true;
        Assert.True(file.Load(log).RememberCheckedProfiles);
    }

    [Fact]
    public void ABrokenFileIsOnlyReplacedWhenTheUserChangesASetting()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "{ broken");

        var settings = SettingsViewModel.Load(new SettingsFile(FilePath), log);
        settings.CheckedProfiles([], []); // Remember is off (the default): nothing to keep, nothing written.

        Assert.Equal("{ broken", File.ReadAllText(FilePath));

        settings.OfferBetas = false;

        Assert.False(new SettingsFile(FilePath).Load(log).OfferBetas);
    }

    [Fact]
    public void AFailedWriteIsLoggedAndTheSettingStillApplies()
    {
        Directory.CreateDirectory(FilePath); // A folder where the file should be: every write fails.
        var settings = SettingsViewModel.Load(new SettingsFile(FilePath), log);

        settings.OfferBetas = false;

        Assert.False(settings.OfferBetas);
        Assert.StartsWith($"Settings could not be saved to {FilePath}: ", log.Entries[^1].Text);
    }
}
