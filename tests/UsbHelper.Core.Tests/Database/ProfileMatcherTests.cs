using UsbHelper.Core.Database;
using UsbHelper.Core.Detection;

namespace UsbHelper.Core.Tests.Database;

public class ProfileMatcherTests
{
    private static HardwareDatabase Database(params (string Id, string Manufacturer, string Model)[] computers)
    {
        using var db = new TestDatabase();
        foreach (var (id, manufacturer, model) in computers)
        {
            db.Write($"computers/{id}.yaml", $"""
                id: {id}
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
        var database = Database(("dell.latitude-7450", "Dell Inc.", "Latitude 7450"), ("lenovo.t14", "LENOVO", "21ML0001"));

        Assert.Equal("dell.latitude-7450", ProfileMatcher.Match(database, new ComputerIdentity("dell inc.", " Latitude 7450 "))!.Id);
    }

    [Theory]
    [InlineData("Dell", "Latitude 7450")] // Manufacturer differs: no guessing.
    [InlineData("Dell Inc.", "Latitude 7440")]
    [InlineData(null, "Latitude 7450")]
    [InlineData("Dell Inc.", null)]
    public void AnythingElseDoesNotMatch(string? manufacturer, string? model)
    {
        var database = Database(("dell.latitude-7450", "Dell Inc.", "Latitude 7450"));

        Assert.Null(ProfileMatcher.Match(database, new ComputerIdentity(manufacturer, model)));
    }

    [Fact]
    public void TwoMatchingProfilesLetTheUserChoose()
    {
        var database = Database(("dell.a", "Dell Inc.", "Latitude 7450"), ("dell.b", "Dell Inc.", "Latitude 7450"));

        Assert.Null(ProfileMatcher.Match(database, new ComputerIdentity("Dell Inc.", "Latitude 7450")));
    }
}
