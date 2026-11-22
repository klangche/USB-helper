using System.Globalization;
using UsbHelper.App.Resources;
using UsbHelper.Core.Database;

namespace UsbHelper.App.Sources;

/// <summary>What the window does for a source entry (#333): the viewer, the browser, a file picker. Tests use a fake.</summary>
public interface ISourceShell
{
    /// <summary>Opens <paramref name="path"/> in the built-in viewer at <paramref name="page"/>, with a notice above it when not null.</summary>
    Task ShowPdfAsync(string path, string title, int? page, string? notice);

    /// <summary>Opens <paramref name="url"/> in the computer's browser, saying <paramref name="message"/> first when not null.</summary>
    Task OpenBrowserAsync(Uri url, string? message);

    /// <summary>Asks the user for a PDF they have; null when they cancel. <paramref name="message"/> says why.</summary>
    Task<string?> PickPdfAsync(string message);

    /// <summary>Says <paramref name="message"/> without opening anything.</summary>
    Task ShowMessageAsync(string message);
}

/// <summary>One entry as the window lists it (#333): its name, a PDF mark, and the page its quote is on.</summary>
public sealed record SourceEntry(SourceFile File, int? Page)
{
    public string Name => File.Name;

    public bool IsPdf => File.IsPdf;

    public string Tip => File.Url?.ToString() ?? Strings.SourceNoDownload;
}

/// <summary>Routes a source entry to the viewer or the browser (#333), from what <see cref="SourceOpener"/> found.</summary>
public static class SourceRouter
{
    /// <summary>Every entry of every source of a v4 guide, in the order listed, each PDF with the page of its first quote.</summary>
    public static IReadOnlyList<SourceEntry> EntriesOf(Guide? guide) =>
        [.. (guide?.SourceDefinitions.Values ?? []).SelectMany(source => source.Files.Select(file =>
            new SourceEntry(file, source.Quotes.FirstOrDefault(quote => quote.File == file.Name && quote.Page != null)?.Page)))];

    public static async Task OpenAsync(SourceOpener opener, ISourceShell shell, SourceEntry entry, CancellationToken cancel = default)
    {
        var opening = await opener.OpenAsync(entry.File, entry.Page, cancel);
        if (opening.Target == SourceTarget.LocalFile)
        {
            if (await shell.PickPdfAsync(Strings.SourceNoDownload) is not { } path)
            {
                return;
            }
            opening = opener.OpenLocal(entry.File, path, entry.Page);
            if (opening.Target == SourceTarget.LocalFile)
            {
                await shell.ShowMessageAsync(opening.Notice == SourceNotice.NotPdf ? Strings.SourceLocalNotPdf : Strings.SourceLocalMismatch);
                return;
            }
        }
        switch (opening.Target)
        {
            case SourceTarget.Viewer:
                await shell.ShowPdfAsync(opening.Path!, entry.Name, opening.Page, opening.Notice == SourceNotice.Changed ? Strings.SourceChanged : null);
                break;
            case SourceTarget.Browser:
                await shell.OpenBrowserAsync(opening.Url!, opening.Notice switch
                {
                    SourceNotice.DownloadFailed => string.Format(CultureInfo.CurrentCulture, Strings.SourceDownloadFailed, opening.Reason),
                    SourceNotice.NotPdf => Strings.SourceNotPdf,
                    _ => null,
                });
                break;
        }
    }
}
