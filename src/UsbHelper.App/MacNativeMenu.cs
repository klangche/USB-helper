using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using UsbHelper.App.Resources;
using UsbHelper.App.ViewModels;

namespace UsbHelper.App;

/// <summary>
/// The window menu's items for macOS's system menu bar (#178). Quit and the application menu come from macOS itself, so
/// there is no Exit item. There is no Edit menu (#179): macOS adds AutoFill, Start Dictation, Emoji &amp; Symbols and Writing
/// Tools to any menu titled Edit, and "Copy tree as text" is the only item, so it sits in File instead.
/// </summary>
public static class MacNativeMenu
{
    // macOS treats a menu titled exactly "Help" as the help menu and adds a search field to it (#180). A zero-width space
    // after the title looks the same but is not recognised.
    public const string HelpTitle = "Help\u200B";

    /// <summary>
    /// Makes the application menu Quit only: no About Avalonia, Services, Hide or Show All (#180, #181). Avalonia.Native
    /// builds its default "About Avalonia" menu while the platform starts, before the app's own code runs, and exports it
    /// then; a menu set on the application afterwards is never exported. So the existing menu is changed in place, which
    /// the exporter follows.
    /// </summary>
    public static void SetAppMenu(Application app, Action quit)
    {
        var item = new NativeMenuItem("Quit USB-helper") { Gesture = new KeyGesture(Key.Q, KeyModifiers.Meta) };
        item.Click += (_, _) => quit();
        if (NativeMenu.GetMenu(app) is { } menu)
        {
            menu.Items.Clear();
            menu.Items.Add(item);
        }
        else
        {
            NativeMenu.SetMenu(app, [item]);
        }
    }

    public static NativeMenu Create(MainViewModel viewModel) =>
    [
        Top(Strings.MenuFile,
            Item(Strings.MenuRefresh, viewModel.RefreshCommand),
            new NativeMenuItemSeparator(),
            Item(Strings.MenuExportProfile, viewModel.ExportProfileCommand),
            Item(Strings.MenuExportLog, viewModel.ExportLogCommand),
            Item(Strings.MenuExportSetup, viewModel.ExportSetupCommand),
            Item(Strings.MenuExportPdf, viewModel.ExportPdfCommand),
            new NativeMenuItemSeparator(),
            Item(Strings.MenuCopyTree, viewModel.CopyTreeCommand),
            new NativeMenuItemSeparator(),
            Item(Strings.MenuSettings, viewModel.SettingsCommand)),
        Top(HelpTitle,
            viewModel.Update is { } update ? Item(Strings.MenuCheckForUpdates, update.CheckNow) : new NativeMenuItem(Label(Strings.MenuCheckForUpdates)) { IsEnabled = false },
            new NativeMenuItemSeparator(),
            Item(Strings.MenuUnlock, viewModel.UnlockCommand),
            Item(Strings.MenuAbout, viewModel.AboutCommand)),
    ];

    // The access-key underscore is a Windows menu convention.
    public static string Label(string header) => header.Replace("_", "");

    private static NativeMenuItem Item(string header, System.Windows.Input.ICommand command) => new(Label(header)) { Command = command };

    private static NativeMenuItem Top(string header, params NativeMenuItemBase[] items)
    {
        var menu = new NativeMenu();
        foreach (var item in items)
        {
            menu.Add(item);
        }
        return new NativeMenuItem(Label(header)) { Menu = menu };
    }
}
