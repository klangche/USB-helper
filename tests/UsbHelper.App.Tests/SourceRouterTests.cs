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

    // From a guide written here, not the real database, so this test needs no particular database data.
    [Fact]
    public void TheEntriesOfAGuideAreListedInOrderWithTheirQuotePages()
    {
        const string Base = "schema_version: 4\nusb: { max_tiers: 7 }\npath_elements:\n"
            + "  root_hub: { found_by: r }\n  computer_hub: { found_by: c }\n  hub: { found_by: h }\n  marked_hub: { found_by: m }\n"
            + "  cable_length: { found_by: l }\n  usb4_router: { found_by: u }\n  device: { found_by: d }\n"
            + "device_kinds:\n  camera: { usb_class: 14 }\n  audio: { usb_class: 1 }\n  hid: { usb_class: 3 }\n";
        const string Guide = "name: USB Standard\nselectable: true\nproav: 0\nsources:\n  paper:\n    files:\n"
            + "      - { name: Page, link: https://example.com/page }\n      - { name: Paper, pdf: https://example.com/p.pdf }\n"
            + "      - { name: Notes, pdf: https://example.com/n.pdf }\n    quotes:\n      - { text: \"five\", file: Paper, page: 12 }\n"
            + "  site:\n    files:\n      - link: https://example.com/\n"
            + "measures:\n  tiers: { counts: [root_hub, hub, device] }\nrules:\n  - { measure: tiers, max: 7 }\n";
        var db = HardwareDatabase.LoadV4(new Dictionary<string, string>
        {
            ["base.yaml"] = Base,
            ["platforms/windows.yaml"] = "name: Windows\nchips:\n  intel: { name: Intel, computer_hubs: 0 }\n",
            ["guides/usb-helper.yaml"] = Guide,
        });

        var entries = SourceRouter.EntriesOf(db.DefaultGuide);

        Assert.Equal([("Page", false, (int?)null), ("Paper", true, 12), ("Notes", true, null), ("example.com", false, null)],
            entries.Select(entry => (entry.Name, entry.IsPdf, entry.Page)));
        Assert.Empty(SourceRouter.EntriesOf(EmbeddedDatabaseTests.V3().DefaultGuide));
    }
}
