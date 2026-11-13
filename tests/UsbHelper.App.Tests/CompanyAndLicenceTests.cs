using System.IO.Compression;
using System.Text;
using UsbHelper.App.Licensing;
using UsbHelper.App.ViewModels;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;
using UsbHelper.Core.Export;
using static UsbHelper.App.Tests.Licensing.UnlockCodeTests;

namespace UsbHelper.App.Tests;

// #230: the company name and the licensed email in the tree text, the log and setup.json; never in the computer profile.
public class CompanyAndLicenceTests
{
    private readonly EventLog log = new();
    private readonly SettingsViewModel settings = new();
    private readonly CodeEntitlements entitlements;
    private readonly MainViewModel viewModel;
    private readonly List<string> copied = [];

    public CompanyAndLicenceTests()
    {
        entitlements = new CodeEntitlements(TestKeys, settings, log);
        viewModel = new MainViewModel(null, [], new ComputerIdentity("Dell Inc.", "Latitude 7450"), log: log, settings: settings, entitlements: entitlements)
        {
            Window = new CopyWindow(copied),
        };
        viewModel.Load([new UsbNode("C1", UsbNodeKind.Controller, "Controller", [])]);
        settings.CompanyName = "Acme AB";
        viewModel.RoomName = " Room 4.12 ";
    }

    private void Unlock() => Assert.Equal(UnlockResult.Unlocked, entitlements.Unlock(TestEmail, TestCode));

    private string CopiedTree()
    {
        viewModel.CopyTreeCommand.Execute(null);
        return copied[^1];
    }

    [Fact]
    public void TheCopiedTreeNamesCompanyRoomAndEmailWhenUnlocked()
    {
        Unlock();

        Assert.Equal(string.Join(Environment.NewLine, "Company: Acme AB", "Room: Room 4.12", $"Licensed to: {TestEmail}", "",
            TreeText.Render(viewModel.Roots[0])), CopiedTree());
    }

    [Fact]
    public void WhileLockedOnlyTheRoomIsNamed()
    {
        Assert.Equal(string.Join(Environment.NewLine, "Room: Room 4.12", "", TreeText.Render(viewModel.Roots[0])), CopiedTree());

        viewModel.RoomName = "";
        Assert.Equal(TreeText.Render(viewModel.Roots[0]), CopiedTree());
    }

    [Fact]
    public void TheLogNamesCompanyAndEmailOnUnlockAndOnEveryChange()
    {
        Assert.DoesNotContain(log.Entries, entry => entry.Text.StartsWith("Company:", StringComparison.Ordinal));

        Unlock();
        settings.CompanyName = "Acme Ltd";
        entitlements.Remove();

        var texts = log.Entries.Select(entry => entry.Text).Where(text => text.StartsWith("Company:", StringComparison.Ordinal)
            || text.StartsWith("Licensed to:", StringComparison.Ordinal));
        Assert.Equal(["Company: Acme AB", $"Licensed to: {TestEmail}", "Company: Acme Ltd", "Company: none", "Licensed to: none"], texts);
    }

    [Fact]
    public void ASavedLicenceIsLoggedAtStart()
    {
        var startLog = new EventLog();
        var saved = new SettingsViewModel();
        saved.Licence(TestEmail, TestCode);

        _ = new MainViewModel(new ComputerIdentity("Dell Inc.", "Latitude 7450"), log: startLog, settings: saved,
            entitlements: new CodeEntitlements(TestKeys, saved, startLog));

        Assert.Contains(startLog.Entries, entry => entry.Text == $"Licensed to: {TestEmail}");
    }

    [Fact]
    public void TheSetupExportHoldsCompanyRoomAndEmail()
    {
        Unlock();

        var files = Export();

        var setup = SetupFile.FromJson(files["setup.json"]);
        Assert.Equal(("Acme AB", "Room 4.12", TestEmail), (setup.CompanyName, setup.RoomName, setup.LicensedTo));
        Assert.StartsWith("Company: Acme AB", files["tree.txt"], StringComparison.Ordinal);
        Assert.StartsWith($"Company: Acme AB{Environment.NewLine}Licensed to: {TestEmail}", files["log.txt"], StringComparison.Ordinal);
    }

    [Fact]
    public void WhileLockedTheSetupExportHasOnlyTheRoom()
    {
        var setup = SetupFile.FromJson(Export()["setup.json"]);

        Assert.Equal((null, "Room 4.12", null), (setup.CompanyName, setup.RoomName, setup.LicensedTo));
    }

    // The computer profile is database data: company-neutral and free of personal data.
    [Fact]
    public void TheComputerProfileHoldsNeitherCompanyNorEmail()
    {
        Unlock();

        var yaml = viewModel.ExportProfile();

        Assert.DoesNotContain("Acme", yaml);
        Assert.DoesNotContain(TestEmail, yaml);
        Assert.DoesNotContain("Room 4.12", yaml);
    }

    private Dictionary<string, string> Export()
    {
        using var zip = new MemoryStream();
        viewModel.ExportSetup(zip, [], new DateOnly(2026, 11, 13), "0.9.0-test");
        zip.Position = 0;
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read);
        return archive.Entries.ToDictionary(entry => entry.FullName, entry =>
        {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            return reader.ReadToEnd();
        });
    }

    private sealed class CopyWindow(List<string> copied) : IMainWindowActions
    {
        public void CopyText(string text) => copied.Add(text);

        public void ExportSetup() { }

        public void ExportPdf() { }

        public void ConfirmRefresh() { }

        public void ExportProfile() { }

        public void ExportLog() { }

        public void ShowSettings() { }

        public void ShowAbout() { }

        public void Exit() { }
    }
}
