using UsbHelper.App.Licensing;
using UsbHelper.App.Settings;
using UsbHelper.App.ViewModels;
using UsbHelper.Core.Diagnostics;
using static UsbHelper.App.Tests.Licensing.UnlockCodeTests;

namespace UsbHelper.App.Tests.Licensing;

/// <summary>Entitlements from the unlock code (#234): saved in settings.json, checked at every start.</summary>
public sealed class CodeEntitlementsTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("usbhelper-licence-tests-").FullName;
    private readonly EventLog log = new();

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private SettingsFile File => new(Path.Combine(folder, "USB-helper", "settings.json"));

    [Fact]
    public void LockedWithoutACode()
    {
        var entitlements = new CodeEntitlements(TestKeys, new SettingsViewModel(), log);

        Assert.False(entitlements.IsUnlocked(Feature.Pro));
        Assert.Null(entitlements.LicensedTo);
    }

    [Fact]
    public void AValidPairUnlocksAndIsSaved()
    {
        var entitlements = new CodeEntitlements(TestKeys, SettingsViewModel.Load(File, log), log);

        Assert.Equal(UnlockResult.Unlocked, entitlements.Unlock(" " + TestEmail, TestCode));

        Assert.True(entitlements.IsUnlocked(Feature.Pro));
        Assert.Equal(TestEmail, entitlements.LicensedTo);
        var saved = File.Load(log);
        Assert.Equal(TestEmail, saved.LicenceEmail);
        Assert.Equal(TestCode, saved.LicenceCode);

        // The next start reads it back and checks it again.
        var next = new CodeEntitlements(TestKeys, SettingsViewModel.Load(File, log), log);
        Assert.True(next.IsUnlocked(Feature.Pro));
        Assert.Equal(TestEmail, next.LicensedTo);
    }

    [Fact]
    public void AWrongPairSavesNothing()
    {
        var entitlements = new CodeEntitlements(TestKeys, SettingsViewModel.Load(File, log), log);

        Assert.Equal(UnlockResult.NoMatch, entitlements.Unlock("other@example.com", TestCode));

        Assert.False(entitlements.IsUnlocked(Feature.Pro));
        Assert.False(System.IO.File.Exists(File.Path));
    }

    [Fact]
    public void ABuildWithoutKeysRefusesAndSaysSo()
    {
        var entitlements = new CodeEntitlements(new Dictionary<int, byte[]>(), new SettingsViewModel(), log);

        Assert.False(entitlements.CodesAvailable);
        Assert.Equal(UnlockResult.NotAvailable, entitlements.Unlock(TestEmail, TestCode));
        Assert.False(entitlements.IsUnlocked(Feature.Pro));
    }

    [Fact]
    public void AnInvalidStoredPairMeansLockedAndALogLine()
    {
        File.Save(new AppSettings { LicenceEmail = "other@example.com", LicenceCode = TestCode });

        var entitlements = new CodeEntitlements(TestKeys, SettingsViewModel.Load(File, log), log);

        Assert.False(entitlements.IsUnlocked(Feature.Pro));
        Assert.Contains(log.Entries, e => e.Text.Contains("unlock code", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(log.Entries, e => e.Text.Contains("other@example.com", StringComparison.Ordinal)); // no email in the log
    }

    [Fact]
    public void ChangedFiresOnUnlockAndOnRemove()
    {
        var settings = SettingsViewModel.Load(File, log);
        var entitlements = new CodeEntitlements(TestKeys, settings, log);
        var changes = 0;
        entitlements.Changed += (_, _) => changes++;

        entitlements.Unlock(TestEmail, TestCode);
        Assert.Equal(1, changes);

        entitlements.Remove();
        Assert.Equal(2, changes);
        Assert.False(entitlements.IsUnlocked(Feature.Pro));
        Assert.Null(entitlements.LicensedTo);
        Assert.Null(File.Load(log).LicenceEmail);
        Assert.Null(File.Load(log).LicenceCode);
    }
}
