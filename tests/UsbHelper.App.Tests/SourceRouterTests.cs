using System.Net;
using System.Security.Cryptography;
using UsbHelper.App.Sources;
using UsbHelper.Core.Database;

namespace UsbHelper.App.Tests;

/// <summary>Where a clicked source entry opens (#333): the viewer, the browser with a message, or a local copy.</summary>
public sealed class SourceRouterTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("usb-helper-router-").FullName;
    private readonly FakeShell shell = new();

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private static readonly byte[] Pdf = "%PDF-1.7 test"u8.ToArray();

    private sealed class FakeShell : ISourceShell
    {
        public List<string> Calls { get; } = [];

        public string? Pick { get; set; }

        public Task ShowPdfAsync(string path, string title, int? page, string? notice)
        {
            Calls.Add($"viewer {title} page {page} notice {notice}");
            return Task.CompletedTask;
        }

        public Task OpenBrowserAsync(Uri url, string? message)
        {
            Calls.Add($"browser {url} message {message}");
            return Task.CompletedTask;
        }

        public Task<string?> PickPdfAsync(string message)
        {
            Calls.Add("pick");
            return Task.FromResult(Pick);
        }

        public Task ShowMessageAsync(string message)
        {
            Calls.Add($"message {message}");
            return Task.CompletedTask;
        }
    }

    private sealed class Answer(Func<HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(answer());
    }

    private SourceOpener Opener(Func<HttpResponseMessage> answer) => new(Path.Combine(folder, "cache"), new HttpClient(new Answer(answer)));

    private static SourceEntry Entry(bool pdf, string? sha = null, string? url = "https://example.com/paper.pdf", int? page = null) =>
        new(new SourceFile("Paper", url == null ? null : new Uri(url), pdf, sha, null), page);

    [Fact]
    public async Task APdfOpensInTheViewerAtItsPageAndALinkInTheBrowser()
    {
        await SourceRouter.OpenAsync(Opener(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Pdf) }), shell, Entry(true, page: 12));
        await SourceRouter.OpenAsync(Opener(() => throw new InvalidOperationException()), shell, Entry(false, url: "https://example.com/page"));

        Assert.Equal(["viewer Paper page 12 notice ", "browser https://example.com/page message "], shell.Calls);
    }

    [Fact]
    public async Task AFailedDownloadOpensTheBrowserWithAMessage()
    {
        await SourceRouter.OpenAsync(Opener(() => new HttpResponseMessage(HttpStatusCode.InternalServerError)), shell, Entry(true));

        var call = Assert.Single(shell.Calls);
        Assert.StartsWith("browser https://example.com/paper.pdf message The PDF could not be downloaded (", call, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AChangedPdfOpensInTheViewerWithTheNotice()
    {
        await SourceRouter.OpenAsync(Opener(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Pdf) }), shell, Entry(true, new string('0', 64)));

        Assert.Equal(["viewer Paper page  notice This source has changed since the guide was written."], shell.Calls);
    }

    [Fact]
    public async Task APdfWithoutADownloadAsksForALocalCopyAndChecksIt()
    {
        var sha = Convert.ToHexStringLower(SHA256.HashData(Pdf));
        var copy = Path.Combine(folder, "copy.pdf");
        File.WriteAllBytes(copy, Pdf);
        var wrong = Path.Combine(folder, "wrong.pdf");
        File.WriteAllBytes(wrong, "%PDF other"u8.ToArray());
        var opener = Opener(() => throw new InvalidOperationException());

        shell.Pick = wrong;
        await SourceRouter.OpenAsync(opener, shell, Entry(true, sha, url: null, page: 12));
        shell.Pick = copy;
        await SourceRouter.OpenAsync(opener, shell, Entry(true, sha, url: null, page: 12));

        Assert.Equal(["pick", "message This file is not the source's PDF: its size or fingerprint does not match.", "pick", "viewer Paper page 12 notice "], shell.Calls);
    }

    [Fact]
    public void TheEntriesOfAGuideAreListedInOrderWithTheirQuotePages()
    {
        var db = DatabaseFormats.Read(4, EmbeddedDatabase.Files()[4]);

        Assert.NotEmpty(SourceRouter.EntriesOf(db.Guides["ochno"]));
        Assert.Empty(SourceRouter.EntriesOf(EmbeddedDatabaseTests.V3().Guides["ochno"]));
    }
}
