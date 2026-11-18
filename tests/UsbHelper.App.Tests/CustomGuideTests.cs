using UsbHelper.App.Licensing;
using UsbHelper.App.Settings;
using UsbHelper.App.ViewModels;
using UsbHelper.Core.Database;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;
using static UsbHelper.App.Tests.Licensing.UnlockCodeTests;

namespace UsbHelper.App.Tests;

/// <summary>
/// Custom (#300): the user's own guide, custom.yaml next to settings.json, last in "USB philosophy" and only while unlocked.
/// </summary>
public sealed class CustomGuideTests : IDisposable
{
    private static readonly HardwareDatabase Database = DatabaseFolder.Load("LENOVO", "21K5", "verified",
        guides: new Dictionary<string, (string, bool)> { ["pure"] = ("Pure", true), ["alpha"] = ("Alpha", true) });

    private readonly string folder = Directory.CreateTempSubdirectory("usb-helper-custom-").FullName;

    private string SettingsPath => Path.Combine(folder, "USB-helper", "settings.json");

    private string CustomPath => Path.Combine(folder, "USB-helper", "custom.yaml");

    public void Dispose() => Directory.Delete(folder, recursive: true);

    /// <summary>The window's actions; records the Custom editor it is asked to open.</summary>
    private sealed class FakeWindow : IMainWindowActions
    {
        public CustomGuideViewModel? Editor { get; private set; }

        public void ShowCustomGuide(CustomGuideViewModel editor) => Editor = editor;

        public void ExportProfile() { }

        public void ExportLog() { }

        public void ShowSettings() { }

        public void ShowUnlock() { }

        public void ShowAbout() { }

        public void Exit() { }

        public void ExportSetup() { }

        public void ExportPdf() { }

        public void ConfirmRefresh() { }

        public void CopyText(string text) { }
    }

    // A start of the app with the settings in the test folder; unlocked by the test code when asked.
    private (MainViewModel Window, FakeWindow View, CodeEntitlements Entitlements, SettingsViewModel Settings) Start(bool unlock)
    {
        var log = new EventLog();
        var settings = SettingsViewModel.Load(new SettingsFile(SettingsPath), log);
        var entitlements = new CodeEntitlements(TestKeys, settings, log);
        if (unlock && entitlements.LicensedTo == null)
        {
            Assert.Equal(UnlockResult.Unlocked, entitlements.Unlock(TestEmail, TestCode));
        }
        var window = new MainViewModel(Database, [], new ComputerIdentity("LENOVO", "21K5", Platform: "os-p"),
            log: log, settings: settings, entitlements: entitlements);
        var view = new FakeWindow();
        window.Window = view;
        return (window, view, entitlements, settings);
    }

    private static GuideOption Custom(MainViewModel window) => window.Guides.Single(option => option.IsCustom);

    // Picks Custom with no custom.yaml yet: the editor opens on the blank; Save makes it the guide.
    private static CustomGuideViewModel SaveBlank(MainViewModel window, FakeWindow view)
    {
        Custom(window).IsSelected = true;
        var editor = view.Editor!;
        editor.SaveCommand.Execute(null);
        window.CustomGuideClosed();
        return editor;
    }

    [Fact]
    public void CustomIsHiddenWhileLocked()
    {
        var (window, _, _, _) = Start(unlock: false);

        Assert.DoesNotContain(window.Guides, option => option.IsCustom || option.Label == "Custom");
    }

    [Fact]
    public void CustomIsListedLastWhileUnlocked()
    {
        var (window, _, _, _) = Start(unlock: true);

        Assert.Equal(["Alpha", "Pure", "USB-Helper", "Custom"], window.Guides.Select(option => option.Label));
        Assert.True(Custom(window).IsEnabled);
        Assert.Equal("USB-Helper", window.Guide!.Name);
    }

    [Fact]
    public void UnlockingAddsCustomLastAndLockingHidesIt()
    {
        var (window, _, entitlements, _) = Start(unlock: false);

        Assert.Equal(UnlockResult.Unlocked, entitlements.Unlock(TestEmail, TestCode));
        Assert.Equal("Custom", window.Guides[^1].Label);

        entitlements.Remove();
        Assert.DoesNotContain(window.Guides, option => option.IsCustom);
    }

    [Fact]
    public void PickingCustomWithoutAFileOpensTheEditorOnTheBlank()
    {
        var (window, view, _, settings) = Start(unlock: true);

        Custom(window).IsSelected = true;

        var editor = Assert.IsType<CustomGuideViewModel>(view.Editor);
        Assert.Equal(editor.Blank, editor.Text);
        Assert.Contains("name: Custom", editor.Text, StringComparison.Ordinal);
        Assert.Contains($"Made by {TestEmail}", editor.Text, StringComparison.Ordinal);
        Assert.Equal("USB-Helper", window.Guide!.Name);

        // Closed without saving: USB-Helper's stays, and the menu shows it again.
        window.CustomGuideClosed();
        Assert.False(Custom(window).IsSelected);
        Assert.Equal(["USB-Helper"], window.Guides.Where(option => option.IsSelected).Select(option => option.Label));
        Assert.Null(settings.CheckedGuide);
        Assert.False(File.Exists(CustomPath));
    }

    [Fact]
    public void SavingAValidGuideWritesCustomYamlAndUsesIt()
    {
        var (window, view, _, settings) = Start(unlock: true);

        var editor = SaveBlank(window, view);

        Assert.Empty(editor.Errors);
        Assert.Equal(editor.Blank, File.ReadAllText(CustomPath));
        Assert.Equal("Custom", window.Guide!.Name);
        Assert.Equal(CustomGuide.Id, window.Guide.Id);
        Assert.True(Custom(window).IsSelected);
        Assert.Equal("Judged by the Custom philosophy", window.GuideText);
        Assert.True(window.HasGuideNoLink);
        Assert.Equal(CustomGuide.Id, settings.CheckedGuide);
        // Every value is empty: USB-Helper's limits apply.
        Assert.Same(Database.DefaultGuide, window.Guide.Limit("os", "p", "hubs.max")!.Guide);
    }

    [Fact]
    public void TheSavedCustomGuideIsUsedAgainAtTheNextStart()
    {
        var (first, view, _, _) = Start(unlock: true);
        SaveBlank(first, view);

        var (window, nextView, _, _) = Start(unlock: true);

        Assert.Equal("Custom", window.Guide!.Name);
        Assert.True(Custom(window).IsSelected);
        Assert.NotNull(Custom(window).Guide);

        // Edit... opens the editor on the saved file.
        Custom(window).EditCommand!.Execute(null);
        Assert.Equal(File.ReadAllText(CustomPath), nextView.Editor!.Text);
    }

    [Fact]
    public void AnInvalidGuideIsNotSavedAndItsProblemsAreListedByLine()
    {
        var (window, view, _, _) = Start(unlock: true);
        var editor = SaveBlank(window, view);
        var used = window.Guide;
        var saved = File.ReadAllText(CustomPath);

        editor.Text = saved.Replace("  tiers:\n    recommended:\n", "  tiers:\n    recommended: many\n", StringComparison.Ordinal);
        editor.SaveCommand.Execute(null);

        var line = Array.IndexOf(editor.Text.Split('\n'), "    recommended: many") + 1;
        Assert.Contains($"custom.yaml:{line}: ", Assert.Single(editor.Errors), StringComparison.Ordinal);
        Assert.True(editor.HasErrors);
        Assert.Equal(saved, File.ReadAllText(CustomPath));
        Assert.Same(used, window.Guide);
    }

    [Fact]
    public void ARememberedCustomGuideFallsBackToUsbHelperWhenLocked()
    {
        var (first, view, entitlements, settings) = Start(unlock: true);
        SaveBlank(first, view);

        // Locked while it runs: hidden, USB-Helper's applies, and the choice is kept.
        entitlements.Remove();
        Assert.Equal("USB-Helper", first.Guide!.Name);
        Assert.DoesNotContain(first.Guides, option => option.IsCustom);
        Assert.Equal(CustomGuide.Id, settings.CheckedGuide);

        // Locked at the next start: the same.
        var (window, _, _, _) = Start(unlock: false);
        Assert.Equal("USB-Helper", window.Guide!.Name);
        Assert.DoesNotContain(window.Guides, option => option.IsCustom);
    }

    [Fact]
    public void ARememberedCustomGuideFallsBackToUsbHelperWhenTheFileIsMissingOrInvalid()
    {
        var (first, view, _, _) = Start(unlock: true);
        SaveBlank(first, view);
        File.Delete(CustomPath);

        var (missing, _, _, _) = Start(unlock: true);
        Assert.Equal("USB-Helper", missing.Guide!.Name);
        Assert.Null(Custom(missing).Guide);
        Assert.Equal(["USB-Helper"], missing.Guides.Where(option => option.IsSelected).Select(option => option.Label));

        File.WriteAllText(CustomPath, "name: Custom\n");
        var (invalid, invalidView, _, _) = Start(unlock: true);
        Assert.Equal("USB-Helper", invalid.Guide!.Name);
        Assert.Null(Custom(invalid).Guide);

        // The editor opens on the invalid file, so it can be fixed.
        Custom(invalid).IsSelected = true;
        Assert.Equal("name: Custom\n", invalidView.Editor!.Text);
    }
}
