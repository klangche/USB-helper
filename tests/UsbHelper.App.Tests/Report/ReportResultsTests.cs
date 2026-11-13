using UsbHelper.App.Report;
using UsbHelper.Core.Diagnostics;

namespace UsbHelper.App.Tests.Report;

// #231: the report's Results section says stable for everything except the problems, grouped over the checked profiles.
public class ReportResultsTests
{
    private static ReportProfile Profile(string label, ConnectionStatus overall, params ReportCheck[] checks) => new(label, overall, checks);

    [Fact]
    public void Profiles_failing_the_same_check_with_the_same_limit_share_one_row()
    {
        var limitations = ReportResults.Limitations(
        [
            Profile("Dell Latitude 7450", ConnectionStatus.Purple, new ReportCheck("Most hops", 2, 3, ConnectionStatus.Purple)),
            Profile("HP EliteBook 840", ConnectionStatus.Purple, new ReportCheck("Most hops", 2, 3, ConnectionStatus.Purple)),
        ]);

        var row = Assert.Single(limitations);
        Assert.Equal<(string, long?, int?, ConnectionStatus)>(("Most hops", 2, 3, ConnectionStatus.Purple), (row.Check, row.Limit, row.Measured, row.Status));
        Assert.Equal(["Dell Latitude 7450", "HP EliteBook 840"], row.Profiles);
    }

    [Fact]
    public void Different_limits_give_separate_rows()
    {
        var limitations = ReportResults.Limitations(
        [
            Profile("Dell Latitude 7450", ConnectionStatus.Purple, new ReportCheck("Most hops", 2, 3, ConnectionStatus.Purple)),
            Profile("HP EliteBook 840", ConnectionStatus.Purple, new ReportCheck("Most hops", 1, 3, ConnectionStatus.Purple)),
        ]);

        Assert.Equal([2L, 1L], limitations.Select(row => row.Limit ?? 0));
    }

    [Fact]
    public void Rows_are_ordered_worst_first_and_stable_checks_are_left_out()
    {
        var limitations = ReportResults.Limitations(
        [
            Profile("Windows on Intel", ConnectionStatus.Red,
                new ReportCheck("External hubs", 4, 2, ConnectionStatus.Green),
                new ReportCheck("Recommended hops", 2, 3, ConnectionStatus.Orange),
                new ReportCheck("Deepest tier", 5, 6, ConnectionStatus.Purple),
                new ReportCheck("Disconnected devices", null, 1, ConnectionStatus.Red)),
        ]);

        Assert.Equal(["Disconnected devices", "Deepest tier", "Recommended hops"], limitations.Select(row => row.Check));
    }

    [Fact]
    public void Limits_not_in_the_database_are_no_problem_rows_but_unknowns_grouped_by_check()
    {
        IReadOnlyList<ReportProfile> profiles =
        [
            Profile("Windows on Intel", ConnectionStatus.Green, new ReportCheck("External hubs", null, 2, ConnectionStatus.Grey), new ReportCheck("Most hops", null, 3, ConnectionStatus.Grey)),
            Profile("macOS on Apple silicon", ConnectionStatus.Green, new ReportCheck("External hubs", null, 2, ConnectionStatus.Grey)),
        ];

        Assert.Empty(ReportResults.Limitations(profiles));
        var unknowns = ReportResults.Unknowns(profiles);
        Assert.Equal(["External hubs", "Most hops"], unknowns.Select(unknown => unknown.Check));
        Assert.Equal(["Windows on Intel", "macOS on Apple silicon"], unknowns[0].Profiles);
    }
}
