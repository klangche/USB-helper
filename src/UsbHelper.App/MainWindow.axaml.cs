using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UsbHelper.App.Resources;
using UsbHelper.App.Settings;
using UsbHelper.App.Sources;
using UsbHelper.App.ViewModels;
using UsbHelper.Core.Diagnostics;

namespace UsbHelper.App;

public partial class MainWindow : Window, IMainWindowActions, ISourceShell
{
    // Source PDFs are fetched only when an entry is clicked (#333), into sources-cache/ next to settings.json.
    private UsbHelper.Core.Database.SourceOpener? sourceOpener;

    private LogWindow? logWindow;
    private UpdateWindow? updateWindow;

    public MainWindow()
    {
        InitializeComponent();
        // Before the tree item, which would otherwise take Enter to expand or collapse.
        Tree.AddHandler(KeyDownEvent, OpenCableMarkByKey, RoutingStrategies.Tunnel);
        // #276: a device row's kind menu, by right-click and by Shift+F10 or the menu key, which raise ContextRequested on
        // the focused item; it bubbles here. Other rows get no menu.
        Tree.AddHandler(ContextRequestedEvent, OpenKindMenu, RoutingStrategies.Bubble);
        // Only controllers collapse (#142): a double-click, arrow key, Enter or subtree collapse closes no other row.
        Tree.AddHandler(TreeViewItem.CollapsedEvent, KeepOpen);
        // A monitor connected or removed (#133): Windows sends WM_DISPLAYCHANGE, which Avalonia passes on as this event.
        Screens.Changed += (_, _) => (DataContext as MainViewModel)?.DisplaysChanged();
        Closed += (_, _) => (BrandingLogo.Source as Bitmap)?.Dispose();
        // The system theme changed while the app runs (#340): the top bar takes that theme's logo, or the name, at once.
        ActualThemeVariantChanged += (_, _) =>
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.Theme = ThemeOf(ActualThemeVariant);
            }
        };
        // Sleep and long sessions (#341): a tick every 30 s notices a clock jump, and activating the window notices an
        // earlier day.
        var sessionTimer = new DispatcherTimer { Interval = SessionWatch.Tick };
        sessionTimer.Tick += (_, _) => (DataContext as MainViewModel)?.SessionTick();
        Opened += (_, _) => sessionTimer.Start();
        Closed += (_, _) => sessionTimer.Stop();
        Activated += (_, _) => (DataContext as MainViewModel)?.SessionActivated();
        // The menu and the top bar buttons run the view model's commands, which call back here.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.Window = this;
                viewModel.Theme = ThemeOf(ActualThemeVariant);
                ShowBrandingLogo(viewModel);
                viewModel.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(MainViewModel.BrandingLogo))
                    {
                        ShowBrandingLogo(viewModel);
                    }
                    if (e.PropertyName == nameof(MainViewModel.SessionOffer) && viewModel.SessionOffer is { } offer)
                    {
                        ShowSessionOffer(viewModel, offer);
                    }
                };
                if (OperatingSystem.IsMacOS())
                {
                    // macOS shows the menu in the system menu bar, not in the window (#178); Windows keeps the window menu.
                    MenuBar.IsVisible = false;
                    NativeMenu.SetMenu(this, MacNativeMenu.Create(viewModel));
                }
                if (viewModel.Update is { } update)
                {
                    update.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName == nameof(UpdateViewModel.IsOffered))
                        {
                            ShowUpdate(update);
                        }
                    };
                }
            }
        };
    }

    // #369: an outcome issue selects its device in the tree.
    private void SelectOutcomeIssue(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: OutcomeIssue issue } && DataContext is MainViewModel viewModel)
        {
            viewModel.SelectDevice(issue.SubjectId);
        }
    }

    // #276: the kind menu of the device row the request came from; built each time, so only device rows have one.
    private void OpenKindMenu(object? sender, ContextRequestedEventArgs e)
    {
        if (e.Source is not Control { DataContext: NodeViewModel { IsDevice: true } item } source || DataContext is not MainViewModel viewModel)
        {
            return;
        }
        MenuItem Choice(string header, UsbHelper.Core.Detection.UsbDeviceKind? kind, bool enabled = true)
        {
            var choice = new MenuItem { Header = header, IsEnabled = enabled };
            choice.Click += (_, _) => viewModel.SelectKind(item.Id, kind);
            return choice;
        }
        var menu = new ContextMenu
        {
            ItemsSource = new object[]
            {
                new MenuItem { Header = Strings.KindMenu, IsEnabled = false },
                Choice(Strings.KindMenuCamera, UsbHelper.Core.Detection.UsbDeviceKind.Camera),
                Choice(Strings.KindMenuAudio, UsbHelper.Core.Detection.UsbDeviceKind.Audio),
                Choice(Strings.KindMenuBoth, UsbHelper.Core.Detection.UsbDeviceKind.CameraAndAudio),
                Choice(Strings.KindMenuNeither, UsbHelper.Core.Detection.UsbDeviceKind.Neither),
                Choice(Strings.KindMenuRemove, null, item.KindByUser),
            },
        };
        menu.Open(source);
        e.Handled = true;
    }

    // #266: hovering a port box shows its connector and highlights the path to it; leaving clears the highlight.
    private void PortBoxEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: NodeViewModel item } && DataContext is MainViewModel viewModel)
        {
            viewModel.HoverPort(item);
        }
    }

    private void PortBoxExited(object? sender, PointerEventArgs e) => (DataContext as MainViewModel)?.HoverPort(null);

    /// <summary>The logo theme of <paramref name="variant"/>: dark for the dark variant, light for anything else (#340).</summary>
    internal static LogoTheme ThemeOf(ThemeVariant variant) => variant == ThemeVariant.Dark ? LogoTheme.Dark : LogoTheme.Light;

    // Read into memory, so the file is not held open and the next logo import can replace it. A file that cannot be
    // read shows no logo.
    private void ShowBrandingLogo(MainViewModel viewModel)
    {
        var old = BrandingLogo.Source as Bitmap;
        BrandingLogo.Source = null;
        old?.Dispose();
        if (viewModel.BrandingLogo is not { } path)
        {
            return;
        }
        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            BrandingLogo.Source = new Bitmap(stream);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    // The update popup (#131) is open exactly while the update model offers something.
    private void ShowUpdate(UpdateViewModel update)
    {
        if (update.IsOffered && updateWindow == null)
        {
            updateWindow = new UpdateWindow { DataContext = update };
            updateWindow.Closed += (_, _) =>
            {
                updateWindow = null;
                if (update.IsOffered)
                {
                    update.Skip.Execute(null); // closed with the title bar's close button or Alt+F4
                }
            };
            _ = updateWindow.ShowDialog(this);
        }
        else if (!update.IsOffered)
        {
            updateWindow?.Close();
        }
    }

    void IMainWindowActions.ExportProfile() => ExportProfile();

    async void IMainWindowActions.ExportLog()
    {
        if (DataContext is MainViewModel viewModel && await LogWindow.SaveLog(this, viewModel.Log) is { } result)
        {
            ExportResult.Text = result;
        }
    }

    void IMainWindowActions.ShowSettings()
    {
        if (DataContext is MainViewModel viewModel)
        {
            _ = new SettingsWindow { DataContext = viewModel.Settings, Unlock = viewModel.Unlock }.ShowDialog(this);
        }
    }

    void IMainWindowActions.ShowUnlock()
    {
        if (DataContext is MainViewModel viewModel)
        {
            UnlockWindow.Open(this, viewModel.Unlock);
        }
    }

    // The session popup (#341): modal, once per break. "Save the log first" opens the usual Save log before clearing it.
    private async void ShowSessionOffer(MainViewModel viewModel, SessionOffer offer)
    {
        var choice = await new SessionWindow(offer).ShowDialog<SessionChoice?>(this);
        if (viewModel.SessionOffer != offer)
        {
            return;
        }
        switch (choice)
        {
            case SessionChoice.SaveThenNewSession:
                // The log is cleared only once it is saved: a cancelled or failed save keeps the session.
                var (message, saved) = await LogWindow.TrySaveLog(this, viewModel.Log);
                ExportResult.Text = saved ? message : message ?? Strings.SessionNotSaved;
                if (saved)
                {
                    viewModel.StartNewSession();
                }
                else
                {
                    viewModel.KeepSession();
                }
                break;
            case SessionChoice.NewSession:
                viewModel.StartNewSession();
                break;
            case SessionChoice.Restart:
                viewModel.Restart();
                break;
            default:
                viewModel.KeepSession();
                break;
        }
    }

    async void IMainWindowActions.ConfirmRefresh()
    {
        if (DataContext is MainViewModel viewModel && await new RefreshWindow().ShowDialog<bool?>(this) is { } clearLog)
        {
            viewModel.RequestRefresh(clearLog);
        }
    }

    async void IMainWindowActions.ShowCustomGuide(CustomGuideViewModel editor)
    {
        await new CustomGuideWindow { DataContext = editor }.ShowDialog(this);
        (DataContext as MainViewModel)?.CustomGuideClosed();
    }

    void IMainWindowActions.ShowAbout() =>
        _ = new AboutWindow { DataContext = new AboutViewModel { Database = (DataContext as MainViewModel)?.DatabaseVersion, DatabaseDetail = (DataContext as MainViewModel)?.DatabaseMarkerDetail } }.ShowDialog(this);

    void IMainWindowActions.Exit() => Close();

    // Takes the screenshot first, so it shows the window as it was when the user chose the menu item. Async void, run by
    // the command: any failure is shown instead of ending the app.
    async void IMainWindowActions.ExportSetup()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }
        try
        {
            var screenshot = Screenshot();
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Strings.ExportSetupTitle,
                SuggestedFileName = viewModel.SetupFileName(DateTime.Now),
                DefaultExtension = "zip",
                FileTypeChoices = [new FilePickerFileType("Zip") { Patterns = ["*.zip"] }],
            });
            if (file == null)
            {
                return;
            }
            await using var stream = await file.OpenWriteAsync();
            viewModel.ExportSetup(stream, screenshot, DateTime.Now);
            ExportResult.Text = string.Format(CultureInfo.CurrentCulture, Strings.ExportSetupSaved, file.Name);
        }
        catch (Exception ex)
        {
            ExportResult.Text = string.Format(CultureInfo.CurrentCulture, Strings.ExportSetupFailed, ex.Message);
        }
    }

    // File > Export PDF (#232). Async void, run by the command: any failure is shown instead of ending the app.
    async void IMainWindowActions.ExportPdf()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Strings.ExportPdfTitle,
                SuggestedFileName = viewModel.PdfFileName(DateTime.Now),
                DefaultExtension = "pdf",
                FileTypeChoices = [new FilePickerFileType("PDF") { Patterns = ["*.pdf"] }],
            });
            if (file == null)
            {
                return;
            }
            await using var stream = await file.OpenWriteAsync();
            viewModel.ExportPdf(stream, DateTime.Now);
            ExportResult.Text = string.Format(CultureInfo.CurrentCulture, Strings.ExportPdfSaved, file.Name);
        }
        catch (Exception ex)
        {
            ExportResult.Text = string.Format(CultureInfo.CurrentCulture, Strings.ExportPdfFailed, ex.Message);
        }
    }

    // The window's content as drawn, at the screen's scale, as PNG.
    private byte[] Screenshot()
    {
        var scale = RenderScaling;
        var size = new PixelSize((int)Math.Ceiling(Bounds.Width * scale), (int)Math.Ceiling(Bounds.Height * scale));
        using var bitmap = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
        bitmap.Render(this);
        using var png = new MemoryStream();
        bitmap.Save(png, new PngBitmapEncoderOptions());
        return png.ToArray();
    }

    // Fire and forget: a clipboard Windows cannot open leaves the old content, nothing else breaks.
    void IMainWindowActions.CopyText(string text) => _ = Clipboard?.SetTextAsync(text);

    // One log window at a time: a second click brings the open one to the front.
    private void OpenLog(object? sender, RoutedEventArgs e)
    {
        if (logWindow != null)
        {
            logWindow.Activate();
            return;
        }
        if (DataContext is MainViewModel viewModel)
        {
            logWindow = new LogWindow(viewModel.Log);
            logWindow.Closed += (_, _) => logWindow = null;
            logWindow.Show(this);
        }
    }

    private static void KeepOpen(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TreeViewItem { DataContext: NodeViewModel node } item && !node.CanExpand)
        {
            item.SetCurrentValue(TreeViewItem.IsExpandedProperty, true);
        }
    }

    // The context menu key (or a right-click) on a tree item opens the mark of its line, the same as clicking its "+".
    private void OpenCableMark(object? sender, ContextRequestedEventArgs e) => e.Handled = ShowCableMark(e.Source);

    // Enter on the selected tree item does the same when its line can be marked: the "+" buttons are not in the Tab
    // order of the tree. Items without a markable line keep Enter for expanding and collapsing.
    private void OpenCableMarkByKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = ShowCableMark(e.Source);
        }
    }

    private static bool ShowCableMark(object? source)
    {
        var item = (source as Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true);
        if (item?.DataContext is NodeViewModel { CanMark: true } node
            && item.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.Name == "CableMark" && button.DataContext == node) is { } plus)
        {
            plus.Flyout?.ShowAt(plus);
            return true;
        }
        return false;
    }

    // Asks where to save, then writes this computer as a computer profile. Async void, run by the command: any failure
    // (no access to the folder, a disk error, a name the encoder rejects) is shown instead of ending the app.
    private async void ExportProfile()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Strings.ExportTitle,
                SuggestedFileName = viewModel.ExportFileName,
                DefaultExtension = "yaml",
                FileTypeChoices = [new FilePickerFileType("YAML") { Patterns = ["*.yaml"] }],
            });
            if (file == null)
            {
                return;
            }
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(viewModel.ExportProfile());
            ExportResult.Text = string.Format(CultureInfo.CurrentCulture, Strings.ExportSaved, file.Name);
        }
        catch (Exception ex)
        {
            ExportResult.Text = string.Format(CultureInfo.CurrentCulture, Strings.ExportFailed, ex.Message);
        }
    }

    // A source entry was clicked (#333).
    private async void OpenSource(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is SourceEntry entry)
        {
            sourceOpener ??= new UsbHelper.Core.Database.SourceOpener(UsbHelper.Core.Database.SourceOpener.NextTo(SettingsFile.Default.Path), new System.Net.Http.HttpClient());
            await SourceRouter.OpenAsync(sourceOpener, this, entry);
        }
    }

    public async Task ShowPdfAsync(string path, string title, int? page, string? notice)
    {
        if (!PdfViewerWindow.TryOpen(this, path, title, page, notice))
        {
            await ShowMessageAsync(Strings.PdfViewerFailed);
            await Launcher.LaunchFileInfoAsync(new FileInfo(path));
        }
    }

    public async Task OpenBrowserAsync(Uri url, string? message)
    {
        if (message != null)
        {
            await ShowMessageAsync(message);
        }
        await Launcher.LaunchUriAsync(url);
    }

    public async Task<string?> PickPdfAsync(string message)
    {
        await ShowMessageAsync(message);
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.SourceOpenLocal,
            FileTypeFilter = [new FilePickerFileType("PDF") { Patterns = ["*.pdf"] }],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public Task ShowMessageAsync(string message)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.Notice = message;
        }
        return Task.CompletedTask;
    }
}
