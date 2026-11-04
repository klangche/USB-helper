using System.Reflection;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using UsbHelper.App.Resources;
using UsbHelper.App.ViewModels;
using UsbHelper.Core.Detection;
using InputKey = Avalonia.Input.Key;

namespace UsbHelper.App.Tests;

public class MenuTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";

    // Records what the window was asked to do.
    private sealed class FakeWindow : IMainWindowActions
    {
        public List<string> Calls { get; } = [];

        public void ExportProfile() => Calls.Add(nameof(ExportProfile));

        public void ExportLog() => Calls.Add(nameof(ExportLog));

        public void ShowAbout() => Calls.Add(nameof(ShowAbout));

        public void ShowSettings() => Calls.Add(nameof(ShowSettings));

        public void Exit() => Calls.Add(nameof(Exit));

        public void CopyText(string text) => Calls.Add(nameof(CopyText));

        public void ExportSetup() => Calls.Add(nameof(ExportSetup));
    }

    private static MainViewModel Loaded(ComputerIdentity identity, FakeWindow window)
    {
        var viewModel = new MainViewModel(null, [], identity) { Window = window };
        viewModel.Load([], monitors: []);
        return viewModel;
    }

    private static readonly ComputerIdentity Known = new("LENOVO", "21K5");

    [Fact]
    public void EachMenuCommandAsksTheWindowForItsAction()
    {
        var window = new FakeWindow();
        var viewModel = Loaded(Known, window);

        viewModel.ExportProfileCommand.Execute(null);
        viewModel.ExportLogCommand.Execute(null);
        viewModel.SettingsCommand.Execute(null);
        viewModel.AboutCommand.Execute(null);
        viewModel.ExitCommand.Execute(null);
        viewModel.CopyTreeCommand.Execute(null);
        viewModel.ExportSetupCommand.Execute(null);

        Assert.Equal(["ExportProfile", "ExportLog", "ShowSettings", "ShowAbout", "Exit", "CopyText", "ExportSetup"], window.Calls);
    }

    [Fact]
    public void ExportProfileIsEnabledExactlyWhenTheButtonWasBefore()
    {
        var viewModel = new MainViewModel(null, [], Known) { Window = new FakeWindow() };
        var changed = 0;
        viewModel.ExportProfileCommand.CanExecuteChanged += (_, _) => changed++;
        Assert.False(viewModel.CanExport);
        Assert.False(viewModel.ExportProfileCommand.CanExecute(null)); // Still loading.

        viewModel.Load([], monitors: []);

        Assert.True(viewModel.CanExport);
        Assert.True(viewModel.ExportProfileCommand.CanExecute(null));
        Assert.True(changed > 0);
        // No manufacturer or model: no profile id, so no export.
        Assert.False(Loaded(new ComputerIdentity(null, "21K5"), new FakeWindow()).ExportProfileCommand.CanExecute(null));
    }

    [Fact]
    public void TheMenuAndTheButtonExportTheProfileWithTheSameCommand()
    {
        var window = MainWindowXaml();
        var button = window.Descendants(Avalonia + "Button")
            .Single(element => (string?)element.Attribute("Content") == "{x:Static res:Strings.ExportProfile}");
        Assert.Equal("{Binding ExportProfileCommand}", (string?)button.Attribute("Command"));
        Assert.Equal("{Binding ExportProfileCommand}", (string?)MenuItem(window, "MenuExportProfile").Attribute("Command"));
    }

    [Fact]
    public void TheMacMenuBarHasFileAndHelpOnlyWithoutAccessKeysEditOrExit()
    {
        var menu = MacNativeMenu.Create(Loaded(Known, new FakeWindow()));

        var top = menu.Items.OfType<NativeMenuItem>().ToList();
        Assert.Equal(["File", MacNativeMenu.HelpTitle], top.Select(item => item.Header));
        List<string> Headers(int index) => top[index].Menu!.Items.OfType<NativeMenuItem>().Select(item => item.Header!).ToList();
        Assert.Equal(["Export computer profile...", "Export log...", "Export full setup...", "-", "Copy tree as text", "-", "Settings..."], Headers(0));
        Assert.Equal(["Check for updates", "-", "About USB-helper"], Headers(1));
    }

    [Fact]
    public void TheMacHelpMenuIsNotNamedExactlyHelpSoMacOsAddsNoSearchField()
    {
        Assert.NotEqual("Help", MacNativeMenu.HelpTitle);
        Assert.Equal("Help", MacNativeMenu.HelpTitle.TrimEnd('\u200B'));
    }

    [Fact]
    public void TheMacAppMenuHasQuitOnlyEvenWhenAvaloniaAddedItsOwnFirst()
    {
        var app = new Avalonia.Application();
        // What Avalonia.Native does while the platform starts.
        NativeMenu.SetMenu(app, [new NativeMenuItem("About Avalonia")]);

        MacNativeMenu.SetAppMenu(app, () => { });

        var item = Assert.IsType<NativeMenuItem>(Assert.Single(NativeMenu.GetMenu(app)!.Items));
        Assert.Equal("Quit USB-helper", item.Header);
        Assert.Equal(new KeyGesture(InputKey.Q, KeyModifiers.Meta), item.Gesture);
    }

    [Fact]
    public void TheWindowMenuStaysInTheWindowXaml()
    {
        // The window menu is hidden only on macOS, in code; the Windows XAML keeps it visible.
        var menu = MainWindowXaml().Descendants(Avalonia + "Menu").Single();
        Assert.Null(menu.Attribute("IsVisible"));
    }

    [Fact]
    public void TheMenuHasFileAndHelpWithTheirItems()
    {
        var menu = MainWindowXaml().Descendants(Avalonia + "Menu").Single();
        var top = menu.Elements(Avalonia + "MenuItem").Select(Key).ToList();
        Assert.Equal(["MenuFile", "MenuEdit", "MenuHelp"], top);
        Assert.Equal(["MenuExportProfile", "MenuExportLog", "MenuExportSetup", "MenuSettings", "MenuExit"], Items(menu, "MenuFile"));
        Assert.Equal(["MenuCopyTree"], Items(menu, "MenuEdit"));
        Assert.Equal("{Binding CopyTreeCommand}", (string?)MenuItem(MainWindowXaml(), "MenuCopyTree").Attribute("Command"));
        Assert.Equal(["MenuCheckForUpdates", "MenuAbout"], Items(menu, "MenuHelp"));
    }

    [Fact]
    public void SettingsOpensTheSettingsWindow()
    {
        var item = MenuItem(MainWindowXaml(), "MenuSettings");
        Assert.Equal("{Binding SettingsCommand}", (string?)item.Attribute("Command"));
        Assert.Null(item.Attribute("IsEnabled"));
    }

    [Fact]
    public void CheckForUpdatesRunsTheUpdateCheckWhereTheAppHasOne()
    {
        // The update check exists on Windows only (#123); elsewhere Update is null and the item is greyed out.
        var item = MenuItem(MainWindowXaml(), "MenuCheckForUpdates");
        Assert.Equal("{Binding Update.CheckNow}", (string?)item.Attribute("Command"));
        Assert.Equal("{Binding Update, Converter={x:Static ObjectConverters.IsNotNull}}", (string?)item.Attribute("IsEnabled"));
    }

    [Fact]
    public void EveryMenuItemHasItsOwnAccessKey()
    {
        var menu = MainWindowXaml().Descendants(Avalonia + "Menu").Single();
        AssertAccessKeys(menu.Elements(Avalonia + "MenuItem").Select(Key));
        foreach (var top in menu.Elements(Avalonia + "MenuItem"))
        {
            AssertAccessKeys(top.Elements(Avalonia + "MenuItem").Select(Key));
        }
    }

    private static void AssertAccessKeys(IEnumerable<string> keys)
    {
        var accessKeys = keys.Select(key =>
        {
            var text = Strings.ResourceManager.GetString(key, Strings.Culture);
            Assert.NotNull(text);
            var index = text.IndexOf('_');
            Assert.True(index >= 0 && index < text.Length - 1, $"{key}: \"{text}\" has no access key");
            return char.ToUpperInvariant(text[index + 1]);
        }).ToList();
        Assert.Equal(accessKeys.Count, accessKeys.Distinct().Count());
    }

    [Fact]
    public void AboutShowsTheRunningVersion()
    {
        var running = typeof(MainViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion.Split('+')[0];
        var about = new AboutViewModel();
        Assert.Equal(running, about.Version);
        Assert.Contains(running, about.VersionText);
    }

    [Fact]
    public void AboutNamesTheLicenseTheIconAttributionAndTheRepository()
    {
        var about = new AboutViewModel();
        Assert.Equal("USB-helper", about.Name);
        Assert.Contains("MIT", about.License);
        Assert.Contains("Font Awesome", about.IconAttribution);
        Assert.Contains("CC BY 4.0", about.IconAttribution);
        Assert.Equal(new Uri("https://github.com/klangche/USB-helper"), about.Repository);
    }

    private static string Key(XElement item) =>
        ((string?)item.Attribute("Header"))?.Replace("{x:Static res:Strings.", "").TrimEnd('}') ?? "";

    private static List<string> Items(XElement menu, string key) =>
        menu.Elements(Avalonia + "MenuItem").Single(item => Key(item) == key)
            .Elements(Avalonia + "MenuItem").Select(Key).ToList();

    private static XElement MenuItem(XElement window, string key) =>
        window.Descendants(Avalonia + "MenuItem").Single(item => Key(item) == key);

    private static XElement MainWindowXaml()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "UsbHelper.App", "MainWindow.axaml");
            if (File.Exists(candidate))
            {
                return XElement.Load(candidate);
            }
        }
        throw new FileNotFoundException("MainWindow.axaml");
    }
}
