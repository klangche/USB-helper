using Avalonia.Controls;
using UsbHelper.App.Resources;
using UsbHelper.App.ViewModels;

namespace UsbHelper.App;

/// <summary>
/// The same File, Edit and Help items as the window's menu, for macOS's system menu bar (#178). Quit and the application
/// menu come from macOS itself, so there is no Exit item.
/// </summary>
public static class MacNativeMenu
{
    public static NativeMenu Create(MainViewModel viewModel) =>
    [
        Top(Strings.MenuFile,
            Item(Strings.MenuExportProfile, viewModel.ExportProfileCommand),
            Item(Strings.MenuExportLog, viewModel.ExportLogCommand),
            Item(Strings.MenuExportSetup, viewModel.ExportSetupCommand),
            new NativeMenuItemSeparator(),
            Item(Strings.MenuSettings, viewModel.SettingsCommand)),
        Top(Strings.MenuEdit, Item(Strings.MenuCopyTree, viewModel.CopyTreeCommand)),
        Top(Strings.MenuHelp,
            viewModel.Update is { } update ? Item(Strings.MenuCheckForUpdates, update.CheckNow) : new NativeMenuItem(Label(Strings.MenuCheckForUpdates)) { IsEnabled = false },
            new NativeMenuItemSeparator(),
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
