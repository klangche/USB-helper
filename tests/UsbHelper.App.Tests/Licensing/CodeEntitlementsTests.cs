using UsbHelper.App.Licensing;
using UsbHelper.App.Settings;
using UsbHelper.App.ViewModels;
using UsbHelper.Core.Detection;
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

    [Fact]
    public void TheMainWindowGatesBrandingByTheUnlockCode()
    {
        var settings = SettingsViewModel.Load(File, log);
        var entitlements = new CodeEntitlements(TestKeys, settings, log);
        _ = new MainViewModel(new ComputerIdentity(null, null), log: log, settings: settings, entitlements: entitlements);
        settings.CompanyName = "Acme AB";
        Assert.Null(settings.Branding);

        entitlements.Unlock(TestEmail, TestCode);
        Assert.Equal("Acme AB", settings.Branding!.CompanyName);

        entitlements.Remove();
        Assert.Null(settings.Branding);
        Assert.Equal("Acme AB", File.Load(log).CompanyName);
    }

    // #238: a build made with USBH_FREE_UNLOCK=true unlocks the extra features for everyone, without a code.
    [Fact]
    public void AFreeBuildUnlocksWithoutACodeAndLicensesNobody()
    {
        var entitlements = new CodeEntitlements(TestKeys, new SettingsViewModel(), log, freeUnlock: true);

        Assert.True(entitlements.FreeUnlock);
        Assert.True(entitlements.IsUnlocked(Feature.Pro));
        Assert.Null(entitlements.LicensedTo);
    }

    [Fact]
    public void AFreeBuildStillTakesAPersonalCodeAndStaysUnlockedWhenItIsRemoved()
    {
        var entitlements = new CodeEntitlements(TestKeys, new SettingsViewModel(), log, freeUnlock: true);

        Assert.Equal(UnlockResult.Unlocked, entitlements.Unlock(TestEmail, TestCode));
        Assert.Equal(TestEmail, entitlements.LicensedTo);

        entitlements.Remove();
        Assert.True(entitlements.IsUnlocked(Feature.Pro));
        Assert.Null(entitlements.LicensedTo);
    }

    [Fact]
    public void ABuildWithoutTheSwitchIsLocked()
    {
        var entitlements = new CodeEntitlements(TestKeys, new SettingsViewModel(), log);

        Assert.False(entitlements.FreeUnlock);
        Assert.False(entitlements.IsUnlocked(Feature.Pro));
        // The tests are built without USBH_FREE_UNLOCK, like every pull request build.
        Assert.False(BuildInfo.FreeUnlock);
    }
}
