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
        // The extractor ends lines with Environment.NewLine ("\r\n" on Windows); the expected texts use "\n".
        return [.. document.GetPages().Select(page => ContentOrderTextExtractor.GetText(page).ReplaceLineEndings("\n"))];
    }

    [Fact]
    public void The_text_holds_the_header_summary_tree_and_page_numbers()
    {
        var pages = PageTexts(Pdf(ReportSamples.Full(null)));
        var all = string.Join("\n", pages);

        Assert.Contains(ReportSamples.Company, all);
        Assert.Contains(ReportSamples.Room, all);
        Assert.Contains("Latitude 7450", all);
        Assert.Contains("Potentially unstable", all);
        Assert.Contains(ReportSamples.Device, all);
        Assert.Contains($"Page 1 of {pages.Count}", pages[0]);
    }

    // The Results section: from its heading to the next one.
    private static string Results(ReportModel model)
    {
        var all = string.Join("\n", PageTexts(Pdf(model)));
        var start = all.IndexOf("\nResults\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"No Results heading in:\n{all}");
        var end = all.IndexOf("\nDisplays\n", start, StringComparison.Ordinal);
        Assert.True(end >= 0, $"No Displays heading after Results in:\n{all}");
        return all[start..end];
    }

    [Fact]
    public void The_results_list_every_checked_profile_and_group_shared_limitations()
    {
        var results = Results(ReportSamples.Full(null));

        Assert.All(ReportSamples.Profiles().Select(profile => profile.Label), label => Assert.Contains(label, results));
        Assert.Contains("\nmacOS on Apple silicon Stable\n", results);
        Assert.Contains("\nLimitations\n", results);
        // One row for the limitation both computers share, before the less severe one.
        Assert.Contains("\nMost hops 3 2 Unstable Dell Latitude 7450\nHP EliteBook 840 G10\nRecommended hops 3 2 Potentially unstable Windows on Intel\n", results);
        Assert.Single(results.Split('\n'), line => line.StartsWith("Most hops", StringComparison.Ordinal));
        Assert.DoesNotContain("Stable for all checked profiles", results);
    }

    [Fact]
    public void Limits_not_in_the_database_are_only_in_the_grey_note()
    {
        var results = Results(ReportSamples.Full(null));

        Assert.Contains("Not in the database: External hubs (Windows on Intel, macOS on Apple silicon)", results);
        Assert.DoesNotContain(results.Split('\n'), line => line.StartsWith("External hubs", StringComparison.Ordinal));
    }

    [Fact]
    public void When_every_profile_is_stable_the_results_say_so_without_a_table()
    {
        var results = Results(ReportSamples.AllStable(null));

        Assert.Contains("Stable for all checked profiles", results);
        Assert.DoesNotContain("Limitations", results);
        Assert.DoesNotContain("Not in the database", results);
    }

    [Fact]
    public void When_every_profile_is_stable_the_note_still_names_the_unknown_limits()
    {
        var model = ReportSamples.AllStable(null);
        var profiles = model.Profiles.ToList();
        profiles[2] = profiles[2] with { Checks = [.. profiles[2].Checks.Skip(1).Prepend(new ReportCheck("External hubs", null, 2, ConnectionStatus.Grey))] };

        var results = Results(model with { Profiles = profiles });

        Assert.Contains("Stable for all checked profiles", results);
        Assert.Contains("Not in the database: External hubs (Windows on Intel)", results);
    }

    [Fact]
    public void Known_limits_that_could_not_be_counted_are_only_in_the_grey_note()
    {
        var model = ReportSamples.Full(null);
        var profiles = model.Profiles.ToList();
        profiles[0] = profiles[0] with { Checks = [.. profiles[0].Checks.Take(3), new ReportCheck("External displays", 2, null, ConnectionStatus.Grey)] };

        var results = Results(model with { Profiles = profiles });

        Assert.Contains("Could not be counted: External displays (Dell Latitude 7450)", results);
        Assert.DoesNotContain(results.Split('\n'), line => line.StartsWith("External displays", StringComparison.Ordinal));
    }

    [Fact]
    public void A_profile_not_stable_without_a_limitation_points_to_the_findings()
    {
        var model = ReportSamples.Full(null);
        var profiles = model.Profiles.ToList();
        profiles[3] = profiles[3] with { Overall = ConnectionStatus.Purple };

        Assert.Contains("See Findings for the reasons.", Results(model with { Profiles = profiles }));
        Assert.DoesNotContain("See Findings", Results(model));
        Assert.DoesNotContain("See Findings", Results(ReportSamples.AllStable(null)));
    }

    [Fact]
    public void Without_a_checked_profile_the_results_say_so()
    {
        var results = Results(ReportSamples.Full(null) with
        {
            Overall = ConnectionStatus.Grey,
            Profiles = [new ReportProfile("No profile checked", ConnectionStatus.Grey, [])],
        });

        Assert.Contains("\nNo profile checked Unknown", results);
        Assert.DoesNotContain("Stable", results);
        Assert.DoesNotContain("Limitations", results);
    }

    // #232: the licensed email under the date line and in the footer; nothing while locked.
    [Fact]
    public void The_licensed_email_is_in_the_header_and_on_every_page_when_set()
    {
        var pages = PageTexts(Pdf(ReportSamples.Full(null) with { LicensedTo = ReportSamples.Email }));

        Assert.Contains($"14:05 · USB-helper 0.9.0-beta.3\nLicensed to {ReportSamples.Email}\n", pages[0]);
        Assert.All(pages, page => Assert.Contains($"Licensed to {ReportSamples.Email}", page));
        Assert.Equal(pages.Count + 1, string.Join("\n", pages).Split("Licensed to").Length - 1);
    }

    [Fact]
    public void Without_a_licensed_email_the_report_names_none()
    {
        Assert.DoesNotContain("Licensed to", string.Join("\n", PageTexts(Pdf(ReportSamples.Full(null)))));
    }

    // #232: the company name only where no logo is shown; the metadata keeps it.
    [Fact]
    public void With_a_logo_the_company_name_is_neither_in_the_header_nor_in_the_footer()
    {
        var pdf = Pdf(ReportSamples.Full(ReportSamples.WriteLogo()));
        var pages = PageTexts(pdf);

        Assert.DoesNotContain(ReportSamples.Company, string.Join("\n", pages));
        Assert.All(pages, page => Assert.Contains(ReportSamples.Room, page)); // The footer still names the room.
        using var document = PdfDocument.Open(pdf);
        Assert.Equal(ReportSamples.Company, document.Information.Author);
    }

    [Fact]
    public void Without_a_logo_the_company_name_is_in_the_header_and_the_footer()
    {
        var pages = PageTexts(Pdf(ReportSamples.Full(null)));

        Assert.StartsWith($"{ReportSamples.Company}\n", pages[0]);
        Assert.All(pages, page => Assert.Contains($"{ReportSamples.Company} - {ReportSamples.Room}", page));
    }

    // The logo box is 54 x 18 mm; a logo is drawn as large as fits, never stretched.
    [Theory]
    [InlineData(1536, 54, 18)]
    [InlineData(512, 18, 18)]
    public void The_logo_fits_a_3_to_1_box(int width, double expectedWidthMm, double expectedHeightMm)
    {
        using var document = PdfDocument.Open(Pdf(ReportSamples.Full(ReportSamples.WritePngLogo(width))));

        var bounds = Assert.Single(document.GetPage(1).GetImages()).BoundingBox;
        Assert.Equal(expectedWidthMm, bounds.Width / 72 * 25.4, 1);
        Assert.Equal(expectedHeightMm, bounds.Height / 72 * 25.4, 1);
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

    [Theory]
    [InlineData("missing.svg", null)]
    [InlineData("corrupt.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"><rect")]
    [InlineData("empty.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"0\" height=\"0\"/>")]
    [InlineData("corrupt.png", "not a png")]
    [InlineData("missing.png", null)]
    public void A_logo_that_cannot_be_read_gives_a_report_without_logo(string name, string? content)
    {
        var path = ReportSamples.WriteFile(name, content ?? "");
        if (content == null)
        {
            File.Delete(path);
        }

        var pages = PageTexts(Pdf(ReportSamples.Full(path)));

        Assert.Contains(ReportSamples.Company, pages[0]);
    }

    // A section or findings group heading never ends a page, and the sign-off heading stays with its block, whatever
    // the tree's length.
    [Fact]
    public void Headings_stay_with_what_follows_them()
    {
        string[] groups = ["Detected facts", "Verified database facts", "Predicted risks", "Unknowns"];
        for (var devices = 1; devices <= 120; devices++)
        {
            using var document = PdfDocument.Open(Pdf(ReportSamples.Sized(devices)));
            foreach (var page in document.GetPages())
            {
                // The lowest line above the footer.
                var letters = page.Letters.Where(letter => letter.Location.Y > 45).ToList();
                var lowest = letters.Min(letter => letter.Location.Y);
                var last = letters.Where(letter => Math.Abs(letter.Location.Y - lowest) < 1).ToList();
                var text = string.Concat(last.OrderBy(letter => letter.Location.X).Select(letter => letter.Value));
                Assert.False(last.All(letter => letter.PointSize > 10.5 && letter.PointSize < 11.5), $"{devices} devices: heading \"{text}\" ends page {page.Number}");
                Assert.DoesNotContain(text, groups);
            }
            var signOff = document.GetPages().Single(page => page.Text.Contains("Sign-off"));
            Assert.Contains("Installed / checked by", ContentOrderTextExtractor.GetText(signOff));
        }
    }

    // The sample PDFs for the owner's review of the look; CI uploads them as an artifact.
    [Fact]
    public void Writes_the_sample_reports()
    {
        var folder = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "report-samples")).FullName;
        var logo = ReportSamples.WriteCroppedLogo();
        foreach (var (name, model) in new[]
        {
            ("full", ReportSamples.Full(logo)),
            ("no-room", ReportSamples.NoRoom(logo)),
            ("no-branding", ReportSamples.NoBranding()),
            ("long-tree", ReportSamples.LongTree(logo)),
            ("png-logo", ReportSamples.Full(ReportSamples.WritePngLogo(1536))),
            ("all-stable", ReportSamples.AllStable(logo)),
            ("company-no-logo", ReportSamples.Full(null)),
            ("licensed", ReportSamples.Full(logo) with { LicensedTo = ReportSamples.Email }),
        })
        {
            File.WriteAllBytes(Path.Combine(folder, $"{name}.pdf"), Pdf(model));
        }

        Assert.Equal(8, Directory.GetFiles(folder, "*.pdf").Length);
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
        Assert.All(model.Profiles, profile => Assert.Equal(["External hubs", "Deepest tier", "Most hops", "External displays"],
            profile.Checks.Take(4).Select(check => check.Name)));
        Assert.Equal(new ReportDisplay("DELL U2723QE", "HDMI", null), Assert.Single(model.Displays!));
        Assert.Contains(model.Tree, row => row.Text == "KEYBOARD" && row.Level == 6 && row.Status != null);
        Assert.Contains(model.Tree, row => row.Text == "Port 1" && row.PortType == "USB-C");
        Assert.Equal(TreeText.Render(viewModel.Roots[0]).Split(Environment.NewLine).Length, model.Tree.Count);
    }

    private static DiagnosticReport Evaluated(int hops, long maxHops, long recommendedHops, int missing = 0)
    {
        static LimitCheck Limit(string field, int actual, long limit) =>
            new(field, actual, limit, true, null, actual > limit ? LimitState.Exceeded : LimitState.Within);
        var device = new MissingDevice(Device("SPEAKER"), "ROOT|port1", "Port 1", []);
        return new DiagnosticReport(ConnectionStatus.Green, new Dictionary<string, ConnectionStatus>(), Limit("limits.max_hubs", 1, 5),
            Limit("limits.max_tiers", 3, 7), Limit("limits.max_hops", hops, maxHops),
            new DisplayCheck(1, 2, true, null, LimitState.Within, []), [.. Enumerable.Repeat(device, missing)], [])
        {
            RecommendedHops = Limit("limits.recommended_hops", hops, recommendedHops),
        };
    }

    [Fact]
    public void The_report_checks_are_the_apps_judgement()
    {
        Assert.Equal(
        [
            new ReportCheck("External hubs", 5, 1, ConnectionStatus.Green),
            new ReportCheck("Deepest tier", 7, 3, ConnectionStatus.Green),
            new ReportCheck("Most hops", 5, 3, ConnectionStatus.Green),
            new ReportCheck("External displays", 2, 1, ConnectionStatus.Green),
            new ReportCheck("Recommended hops", 2, 3, ConnectionStatus.Orange),
        ], MainViewModel.ReportChecks(Evaluated(hops: 3, maxHops: 5, recommendedHops: 2)));

        // Above the maximum the recommendation is not repeated.
        var exceeded = MainViewModel.ReportChecks(Evaluated(hops: 3, maxHops: 2, recommendedHops: 1));
        Assert.Contains(new ReportCheck("Most hops", 2, 3, ConnectionStatus.Purple), exceeded);
        Assert.DoesNotContain(exceeded, check => check.Name == "Recommended hops");

        Assert.Equal(new ReportCheck("Disconnected devices", null, 1, ConnectionStatus.Red),
            MainViewModel.ReportChecks(Evaluated(hops: 3, maxHops: 5, recommendedHops: 5, missing: 1))[^1]);
    }
}
