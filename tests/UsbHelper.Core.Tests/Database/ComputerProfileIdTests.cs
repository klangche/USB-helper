using UsbHelper.Core.Database;

namespace UsbHelper.Core.Tests.Database;

public class ComputerProfileIdTests
{
    [Fact]
    public void IdIsFixedForAName()
    {
        // Pinned: a change here changes the id of every existing computer profile.
        Assert.Equal("y98inr", ComputerProfileId.From("Dell Inc.", "Latitude 7450"));
    }

    [Fact]
    public void CaseAndSpacesDoNotMatter()
    {
        Assert.Equal(ComputerProfileId.From("Dell Inc.", "Latitude 7450"), ComputerProfileId.From("  dell   INC. ", "latitude\t7450 "));
    }

    [Fact]
    public void DifferentModelsGetDifferentIds()
    {
        Assert.NotEqual(ComputerProfileId.From("Dell Inc.", "Latitude 7450"), ComputerProfileId.From("Dell Inc.", "Latitude 7440"));
        // The separator keeps "A" + "B C" apart from "A B" + "C".
        Assert.NotEqual(ComputerProfileId.From("A", "B C"), ComputerProfileId.From("A B", "C"));
    }

    [Theory]
    [InlineData("Dell Inc.", "Latitude 7450")]
    [InlineData("LENOVO", "21ML0035MX")]
    [InlineData("Apple Inc.", "MacBookPro18,2")]
    [InlineData("x", "y")]
    public void IdIsSixLowercaseLettersOrDigits(string manufacturer, string model)
    {
        Assert.Matches("^[0-9a-z]{6}$", ComputerProfileId.From(manufacturer, model));
    }
}
