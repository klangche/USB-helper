using System.Net;
using System.Security.Cryptography;
using UsbHelper.Core.Database;

namespace UsbHelper.Core.Tests.Database;

/// <summary>Sources with any number of PDFs and links (#333): names, validation, and where each entry opens.</summary>
public sealed class SourceFilesTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("usb-helper-sources-").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    // A v4 database whose Room guide has the given sources block (indented under "sources:").
    private static HardwareDatabase Load(string sources)
    {
        var room = V4Tests.Room.Replace(
            "sources:\n  room: { link: \"https://example.com/room\", title: \"Room guide\", version: \"1.0\" }",
            "sources:\n  room: { title: \"Room guide\", version: \"1.0\" }\n" + sources, StringComparison.Ordinal);
        return HardwareDatabase.LoadV4(new Dictionary<string, string>
        {
            ["base.yaml"] = V4Tests.Base,
            ["platforms/windows.yaml"] = V4Tests.Windows,
            ["platforms/macos.yaml"] = V4Tests.MacOs,
            ["guides/usb-helper.yaml"] = V4Tests.UsbHelper,
            ["guides/room.yaml"] = room,
        });
    }

    private static GuideSource Other(string sources) => Load(sources).Guides["room"].SourceDefinitions["other"];

    private static string Error(string sources) =>
        Assert.Single(Assert.Throws<DatabaseException>(() => Load(sources)).Errors).Message;

    [Fact]
    public void OnePdfOneLinkOrOneOfEach()
    {
        Assert.True(Assert.Single(Other("  other:\n    files:\n      - pdf: https://example.com/a/White%20paper.pdf\n").Files).IsPdf);
        Assert.False(Assert.Single(Other("  other:\n    files:\n      - link: https://example.com/page\n").Files).IsPdf);
        var both = Other("  other:\n    files:\n      - { name: Paper, pdf: https://example.com/p.pdf }\n      - { name: Page, link: https://example.com/page }\n").Files;
        Assert.Equal([("Paper", true), ("Page", false)], both.Select(file => (file.Name, file.IsPdf)));
    }

    [Fact]
    public void FourLinksAndTwoPdfsAreAllShownInOrder()
    {
        var files = Other("  other:\n    files:\n" + string.Concat(
            new[] { ("L1", "link"), ("P1", "pdf"), ("L2", "link"), ("L3", "link"), ("P2", "pdf"), ("L4", "link") }
                .Select((each, i) => $"      - {{ name: {each.Item1}, {each.Item2}: https://example.com/{i} }}\n"))).Files;

        Assert.Equal(["L1", "P1", "L2", "L3", "P2", "L4"], files.Select(file => file.Name));
    }

    [Theory]
    [InlineData("https://example.com/a/White%20paper.pdf", "White paper.pdf")]
    [InlineData("https://ochno.atlassian.net/wiki/spaces/~ocs/pages/1114996744/Guide+USB+hub+chains+in+video+conference+room", "Guide USB hub chains in video conference room")]
    [InlineData("https://example.com/", "example.com")]
    public void OneEntryWithoutANameShowsTheDecodedFileNameOrTheHost(string url, string name)
    {
        Assert.Equal(name, Assert.Single(Other($"  other:\n    files:\n      - link: {url}\n").Files).Name);
    }

    [Theory]
    [InlineData("      - { link: https://example.com/a }\n      - { name: B, link: https://example.com/b }\n", "every entry needs its own 'name'")]
    [InlineData("      - { name: A, link: https://example.com/a }\n      - { name: A, link: https://example.com/b }\n", "the name 'A' is used twice")]
    [InlineData("      - { name: A, link: https://example.com/a, pdf: https://example.com/a.pdf }\n", "exactly one of 'pdf' and 'link'")]
    [InlineData("      - { name: A }\n", "exactly one of 'pdf' and 'link'")]
    public void BadEntriesAreErrors(string entries, string message)
    {
        Assert.Contains(message, Error("  other:\n    files:\n" + entries), StringComparison.Ordinal);
    }

    [Fact]
    public void AQuotePointsAtItsEntryAndPage()
    {
        var source = Other("  other:\n    files:\n      - { name: Paper, pdf: https://example.com/p.pdf }\n      - { name: Page, link: https://example.com/page }\n"
            + "    quotes:\n      - { text: \"five hubs\", file: Paper, page: 12 }\n");

        Assert.Equal(new SourceQuote("five hubs", "Paper", 12), Assert.Single(source.Quotes));
    }

    private static readonly byte[] Pdf = "%PDF-1.7 test"u8.ToArray();

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private SourceOpener Opener(Func<HttpResponseMessage> answer, List<Uri>? asked = null) =>
        new(Path.Combine(folder, "sources-cache"), new HttpClient(new Answer(request =>
        {
            asked?.Add(request.RequestUri!);
            return answer();
        })));

    private sealed class Answer(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(answer(request));
    }

    private static HttpResponseMessage Ok(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private static SourceFile PdfFile(string? sha = null, long? size = null) => new("Paper", new Uri("https://example.com/p.pdf"), true, sha, size);

    [Fact]
    public async Task ALinkOpensInTheBrowserAndAPdfInTheViewer()
    {
        var link = new SourceFile("Page", new Uri("https://example.com/page"), false, null, null);

        Assert.Equal(SourceTarget.Browser, (await Opener(() => throw new InvalidOperationException()).OpenAsync(link, null, default)).Target);
        var opened = await Opener(() => Ok(Pdf)).OpenAsync(PdfFile(Sha(Pdf), Pdf.Length), 12, default);
        Assert.Equal(SourceTarget.Viewer, opened.Target);
        Assert.Equal(12, opened.Page);
        Assert.Equal(SourceNotice.None, opened.Notice);
        Assert.Equal(Pdf, File.ReadAllBytes(opened.Path!));
    }

    [Fact]
    public async Task AFailedDownloadOpensTheBrowserWithAMessage()
    {
        var opened = await Opener(() => new HttpResponseMessage(HttpStatusCode.NotFound)).OpenAsync(PdfFile(), null, default);

        Assert.Equal(SourceTarget.Browser, opened.Target);
        Assert.Equal(SourceNotice.DownloadFailed, opened.Notice);
        Assert.Equal(new Uri("https://example.com/p.pdf"), opened.Url);
    }

    [Fact]
    public async Task AWrongFingerprintOpensTheViewerWithTheChangedNotice()
    {
        var opened = await Opener(() => Ok(Pdf)).OpenAsync(PdfFile(new string('0', 64)), null, default);

        Assert.Equal(SourceTarget.Viewer, opened.Target);
        Assert.Equal(SourceNotice.Changed, opened.Notice);
    }

    [Fact]
    public async Task AFileWithoutPdfOpensInTheBrowser()
    {
        var opened = await Opener(() => Ok("<html>login</html>"u8.ToArray())).OpenAsync(PdfFile(), null, default);

        Assert.Equal(SourceTarget.Browser, opened.Target);
        Assert.Equal(SourceNotice.NotPdf, opened.Notice);
    }

    [Fact]
    public async Task ACachedPdfOpensOffline()
    {
        var asked = new List<Uri>();
        await Opener(() => Ok(Pdf), asked).OpenAsync(PdfFile(Sha(Pdf)), null, default);

        var offline = await Opener(() => throw new HttpRequestException("offline"), asked).OpenAsync(PdfFile(Sha(Pdf)), 3, default);

        Assert.Single(asked);
        Assert.Equal(SourceTarget.Viewer, offline.Target);
        Assert.Equal(3, offline.Page);
    }

    // A PDF without a public download: the user opens a copy, which is checked and kept.
    [Fact]
    public async Task ALocalCopyIsCheckedAgainstTheFingerprint()
    {
        var file = new SourceFile("Paper", null, true, Sha(Pdf), Pdf.Length);
        var opener = Opener(() => throw new InvalidOperationException());
        var good = Path.Combine(folder, "good.pdf");
        var wrong = Path.Combine(folder, "wrong.pdf");
        File.WriteAllBytes(good, Pdf);
        File.WriteAllBytes(wrong, "%PDF-1.7 other"u8.ToArray());

        Assert.Equal(SourceTarget.LocalFile, (await opener.OpenAsync(file, null, default)).Target);
        Assert.Equal(SourceNotice.LocalMismatch, opener.OpenLocal(file, wrong, null).Notice);
        Assert.Equal(SourceTarget.Viewer, opener.OpenLocal(file, good, 12).Target);
        Assert.Equal(SourceTarget.Viewer, (await opener.OpenAsync(file, null, default)).Target);
    }
}
