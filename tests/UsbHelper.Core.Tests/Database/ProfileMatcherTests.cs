using UsbHelper.Core.Database;
using UsbHelper.Core.Detection;

namespace UsbHelper.Core.Tests.Database;

public class ProfileMatcherTests
{
    private static HardwareDatabase Database(params (string Manufacturer, string Model)[] computers)
    {
        using var db = new TestDatabase();
        foreach (var (manufacturer, model) in computers)
        {
            db.Write($"computers/{model}.yaml", $"""
                inherits: test-platform
                manufacturer: {manufacturer}
                model: {model}
                limits:
                  max_hubs: null
                  max_tiers: null
                  max_hops: null
                """);
        }
        return db.Load();
    }

    [Fact]
    public void ExactManufacturerAndModelMatch()
    {
        var database = Database(("Dell Inc.", "Latitude 7450"), ("LENOVO", "21ML0001"));

        Assert.Equal(ComputerProfileId.From("Dell Inc.", "Latitude 7450"),
            ProfileMatcher.Match(database, new ComputerIdentity("dell inc.", " Latitude 7450 "))!.Id);
    }

    [Theory]
    [InlineData("Dell", "Latitude 7450")] // Manufacturer differs: no guessing.
    [InlineData("Dell Inc.", "Latitude 7440")]
    [InlineData(null, "Latitude 7450")]
    [InlineData("Dell Inc.", null)]
    public void AnythingElseDoesNotMatch(string? manufacturer, string? model)
    {
        var database = Database(("Dell Inc.", "Latitude 7450"));

        Assert.Null(ProfileMatcher.Match(database, new ComputerIdentity(manufacturer, model)));
    }
}
