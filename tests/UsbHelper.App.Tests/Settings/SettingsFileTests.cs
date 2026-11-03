using UsbHelper.App.Settings;
using UsbHelper.Core.Diagnostics;

namespace UsbHelper.App.Tests.Settings;

/// <summary>settings.json (#126): defaults without a file, a round trip, and a broken file that is logged and left alone.</summary>
public sealed class SettingsFileTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("usbhelper-settings-tests-").FullName;
    private readonly EventLog log = new();

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private string FilePath => Path.Combine(folder, "USB-helper", "settings.json");

    [Fact]
    public void TheFileIsInTheUsersRoamingAppDataFolder()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        Assert.Equal(Path.Combine(appData, "USB-helper", "settings.json"), SettingsFile.Default.Path);
    }

    [Fact]
    public void NoFileMeansDefaults()
    {
        var settings = new SettingsFile(FilePath).Load(log);

        Assert.True(settings.CheckForUpdatesAtStart);
        Assert.True(settings.OfferBetas);
        Assert.False(settings.RememberCheckedProfiles);
        Assert.Empty(settings.CheckedOperatingSystems);
        Assert.Empty(settings.CheckedComputers);
        Assert.False(File.Exists(FilePath)); // Nothing is written until a setting changes.
    }

    [Fact]
    public void WhatIsSavedIsLoaded()
    {
        var file = new SettingsFile(FilePath);
        var saved = new AppSettings
        {
            CheckForUpdatesAtStart = false,
            OfferBetas = false,
            RememberCheckedProfiles = true,
            CheckedOperatingSystems = ["windows-amd"],
            CheckedComputers = ["lenovo-21k5", "dell-xps"],
        };

        file.Save(saved);
        var loaded = file.Load(log);

        Assert.False(loaded.CheckForUpdatesAtStart);
        Assert.False(loaded.OfferBetas);
        Assert.True(loaded.RememberCheckedProfiles);
        Assert.Equal(["windows-amd"], loaded.CheckedOperatingSystems);
        Assert.Equal(["lenovo-21k5", "dell-xps"], loaded.CheckedComputers);
    }

    [Fact]
    public void AMissingSettingKeepsItsDefault()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, """{ "offerBetas": false }""");

        var settings = new SettingsFile(FilePath).Load(log);

        Assert.True(settings.CheckForUpdatesAtStart);
        Assert.False(settings.OfferBetas);
    }

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("""{ "offerBetas": "maybe" }""")]
    [InlineData("null")]
    [InlineData("")]
    public void ABrokenFileGivesDefaultsIsLoggedAndIsLeftAsItIs(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, content);

        var settings = new SettingsFile(FilePath).Load(log);

        Assert.True(settings.CheckForUpdatesAtStart);
        Assert.True(settings.OfferBetas);
        Assert.False(settings.RememberCheckedProfiles);
        Assert.Contains(log.Entries, entry => entry.Text.StartsWith($"Settings file {FilePath} could not be read", StringComparison.Ordinal)
            && entry.Text.EndsWith("; using the defaults", StringComparison.Ordinal));
        Assert.Equal(content, File.ReadAllText(FilePath));
    }
}
