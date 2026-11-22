using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using UsbHelper.App.Resources;

namespace UsbHelper.App;

/// <summary>
/// The built-in PDF viewer (#333). PDFium (through PDFtoImage) draws each page; the window scrolls, zooms, shows the
/// page number and saves a copy. When PDFium cannot load, <see cref="TryOpen"/> says so and the caller opens the
/// system's PDF app instead.
/// </summary>
public partial class PdfViewerWindow : Window
{
    private static readonly double[] Zooms = [0.5, 0.75, 1, 1.25, 1.5, 2];

    private readonly byte[] pdf = [];
    private readonly int pageCount;
    private readonly string title = "";
    private int zoom = 2;

    public PdfViewerWindow()
    {
        InitializeComponent();
    }

    private PdfViewerWindow(byte[] pdf, int pageCount, string title, string? notice) : this()
    {
        this.pdf = pdf;
        this.pageCount = pageCount;
        this.title = title;
        Title = title;
        NoticeText.Text = notice;
        NoticeBar.IsVisible = notice != null;
        Scroller.ScrollChanged += (_, _) => ShowPage();
    }

    /// <summary>
    /// Opens <paramref name="path"/> at <paramref name="page"/> (1 for the first); false when PDFium cannot load or read
    /// the file, so the caller falls back to the system's PDF app.
    /// </summary>
    public static bool TryOpen(Window owner, string path, string title, int? page, string? notice)
    {
        // PDFium is built for these systems only; elsewhere the system's PDF app opens it.
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
        {
            return false;
        }
        PdfViewerWindow window;
        try
        {
            var bytes = File.ReadAllBytes(path);
            window = new PdfViewerWindow(bytes, PDFtoImage.Conversion.GetPageCount(bytes), title, notice);
            window.Render();
        }
        catch (Exception e) when (e is DllNotFoundException or TypeInitializationException or BadImageFormatException or EntryPointNotFoundException
            or IOException or InvalidOperationException or NotSupportedException or PlatformNotSupportedException)
        {
            return false;
        }
        window.Show(owner);
        if (page is > 1)
        {
            window.Opened += (_, _) => window.GoTo(page.Value);
        }
        return true;
    }

    // Every page at the current zoom; a page is drawn at 96 dpi times the zoom.
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private void Render()
    {
        Pages.Children.Clear();
        for (var index = 0; index < pageCount; index++)
        {
            using var bitmap = PDFtoImage.Conversion.ToImage(pdf, index, null, new PDFtoImage.RenderOptions(Dpi: (int)(96 * Zooms[zoom])));
            using var data = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            using var stream = data.AsStream();
            Pages.Children.Add(new Image { Source = new Bitmap(stream), Stretch = Avalonia.Media.Stretch.None });
        }
        ZoomText.Text = string.Format(CultureInfo.CurrentCulture, Strings.PdfZoom, (int)(Zooms[zoom] * 100));
        ShowPage();
    }

    private void GoTo(int page)
    {
        if (page >= 1 && page <= Pages.Children.Count)
        {
            Pages.Children[page - 1].BringIntoView();
        }
    }

    // The page whose top is nearest above the middle of the view.
    private void ShowPage()
    {
        var middle = Scroller.Offset.Y + Scroller.Viewport.Height / 2;
        var current = 1;
        for (var index = 0; index < Pages.Children.Count; index++)
        {
            if (Pages.Children[index].Bounds.Top <= middle)
            {
                current = index + 1;
            }
        }
        PageText.Text = string.Format(CultureInfo.CurrentCulture, Strings.PdfPage, current, pageCount);
    }

    private void ZoomIn(object? sender, RoutedEventArgs e) => SetZoom(zoom + 1);

    private void ZoomOut(object? sender, RoutedEventArgs e) => SetZoom(zoom - 1);

    private void SetZoom(int value)
    {
        if ((OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux()) && value >= 0 && value < Zooms.Length && value != zoom)
        {
            zoom = value;
            Render();
        }
    }

    private async void SaveCopy(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = title.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? title : $"{title}.pdf",
            FileTypeChoices = [new FilePickerFileType("PDF") { Patterns = ["*.pdf"] }],
        });
        if (file != null)
        {
            await using var stream = await file.OpenWriteAsync();
            await stream.WriteAsync(pdf);
        }
    }
}
