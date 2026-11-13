using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UsbHelper.App.Report;
using UsbHelper.App.ViewModels;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;

namespace UsbHelper.App.Tests.Report;

// #231: the PDF sign-off report. Text is real text in an embedded font, so it can be extracted, selected and copied.
public class ReportPdfTests
{
    private static byte[] Pdf(ReportModel model)
    {
        using var output = new MemoryStream();
        ReportPdf.Write(output, model);
        return output.ToArray();
    }

    private static List<string> PageTexts(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return [.. document.GetPages().Select(page => ContentOrderTextExtractor.GetText(page))];
    }

    [Fact]
    public void The_text_holds_the_header_summary_tree_and_page_numbers()
    {
        var pages = PageTexts(Pdf(ReportSamples.Full(ReportSamples.WriteLogo())));
        var all = string.Join("\n", pages);

        Assert.Contains(ReportSamples.Company, all);
        Assert.Contains(ReportSamples.Room, all);
        Assert.Contains("Latitude 7450", all);
        Assert.Contains("Potentially unstable", all);
        Assert.Contains(ReportSamples.Device, all);
        Assert.Contains($"Page 1 of {pages.Count}", pages[0]);
    }

    [Fact]
    public void Without_a_room_name_the_header_says_so()
    {
        var all = string.Join("\n", PageTexts(Pdf(ReportSamples.NoRoom(null))));

        Assert.Contains("Room: not entered", all);
    }

    [Fact]
    public void Without_logo_and_company_the_header_text_starts_at_the_left_margin()
    {
        using var document = PdfDocument.Open(Pdf(ReportSamples.NoBranding()));
        var title = document.GetPage(1).GetWords().First(word => word.Text == "USB");

        Assert.Equal(18 / 25.4 * 72, title.BoundingBox.Left, 0);
    }

    [Fact]
    public void A_long_tree_breaks_across_pages_and_keeps_the_sign_off_whole()
    {
        var pages = PageTexts(Pdf(ReportSamples.LongTree(null)));

        Assert.True(pages.Count >= 4, $"{pages.Count} pages");
        Assert.Contains(pages, page => page.Contains("USB tree (continued)"));
        var signOff = Assert.Single(pages, page => page.Contains("Installed / checked by"));
        Assert.Contains("Accepted by", signOff);
        Assert.Contains("Notes", signOff);
        Assert.Equal(2, signOff.Split("Signature").Length - 1);
    }

    [Fact]
    public void Pages_are_A4()
    {
        using var document = PdfDocument.Open(Pdf(ReportSamples.Full(null)));

        Assert.All(document.GetPages(), page =>
        {
            Assert.Equal(595, page.Width, 0);
            Assert.Equal(842, page.Height, 0);
        });
    }

    [Fact]
    public void The_metadata_names_the_room_and_the_company()
    {
        using var document = PdfDocument.Open(Pdf(ReportSamples.Full(null)));

        Assert.Equal($"USB setup sign-off - {ReportSamples.Room}", document.Information.Title);
        Assert.Equal(ReportSamples.Company, document.Information.Author);
        Assert.Equal("USB-helper 0.9.0-beta.3", document.Information.Creator);
    }

    [Fact]
    public void Without_a_room_the_title_names_the_computer()
    {
        using var document = PdfDocument.Open(Pdf(ReportSamples.NoRoom(null)));

        Assert.Equal("USB setup sign-off - Dell Inc. Latitude 7450", document.Information.Title);
    }

    // The sample PDFs for the owner's review of the look; CI uploads them as an artifact.
    [Fact]
    public void Writes_the_sample_reports()
    {
        var folder = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "report-samples")).FullName;
        var logo = ReportSamples.WriteLogo();
        foreach (var (name, model) in new[]
        {
            ("full", ReportSamples.Full(logo)),
            ("no-room", ReportSamples.NoRoom(logo)),
            ("no-branding", ReportSamples.NoBranding()),
            ("long-tree", ReportSamples.LongTree(logo)),
        })
        {
            File.WriteAllBytes(Path.Combine(folder, $"{name}.pdf"), Pdf(model));
        }

        Assert.Equal(4, Directory.GetFiles(folder, "*.pdf").Length);
    }

    private static UsbNode Device(string id) => new(id, UsbNodeKind.Device, id, []);

    private static UsbNode Port(string hubId, int number, UsbNode? child) =>
        new($"{hubId}|port{number}", UsbNodeKind.Port, $"Port {number}", child == null ? [] : [child]) { PortNumber = number };

    private static UsbNode Hub(string id, params UsbNode?[] ports) =>
        new(id, UsbNodeKind.Hub, id, ports.Select((child, index) => Port(id, index + 1, child)).ToList());

    [Fact]
    public void The_model_is_built_from_the_current_setup()
    {
        var root = Hub("ROOT", Hub("DOCK", Device("KEYBOARD")), Device("MOUSE"));
        root = root with { Children = [root.Children[0] with { UsbC = true, UserConnectable = true }, root.Children[1] with { UserConnectable = true }] };
        var viewModel = new MainViewModel(DatabaseFolder.Load("Dell Inc.", "Latitude 7450", status: "verified"), [],
            new ComputerIdentity("Dell Inc.", "Latitude 7450", Platform: "p", MachineName: "ASSET-0042"),
            () => [new DisplayMonitor("DELL", "DELL U2723QE", MonitorOutput.Hdmi)]);
        viewModel.Load([new UsbNode("C1", UsbNodeKind.Controller, "Controller", [root])]);
        viewModel.Computers.Single().IsChecked = true;
        viewModel.RoomName = "  Room 1  ";

        var model = viewModel.Report(new DateTime(2026, 11, 13, 9, 30, 0), ReportSamples.Company, null, "0.9.0");

        Assert.Equal("Room 1", model.RoomName);
        Assert.Null(model.Computer.MachineName);
        Assert.Equal("Latitude 7450", model.Computer.Model);
        Assert.Equal(1, model.Counts.Hubs);
        Assert.Equal(2, model.Profiles.Count); // The detected platform's profile is checked at start.
        Assert.All(model.Profiles, profile => Assert.Equal(4, profile.Checks.Count));
        Assert.Equal(new ReportDisplay("DELL U2723QE", "HDMI", null), Assert.Single(model.Displays!));
        Assert.Contains(model.Tree, row => row.Text == "KEYBOARD" && row.Level == 6 && row.Status != null);
        Assert.Contains(model.Tree, row => row.Text == "Port 1" && row.PortType == "USB-C");
        Assert.Equal(TreeText.Render(viewModel.Roots[0]).Split(Environment.NewLine).Length, model.Tree.Count);
    }
}
