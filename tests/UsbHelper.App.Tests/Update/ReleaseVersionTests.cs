using UsbHelper.App.Update;

namespace UsbHelper.App.Tests.Update;

/// <summary>Release versions compared as semantic versions (#111).</summary>
public class ReleaseVersionTests
{
    [Theory]
    [InlineData("0.6.0", "0.7.0")]
    [InlineData("0.6.9", "0.6.10")]
    [InlineData("0.7.0-beta.2", "0.7.0")] // a release is newer than its betas
    [InlineData("0.7.0-beta.2", "0.7.0-beta.10")]
    [InlineData("0.6.0", "0.7.0-beta.1")]
    [InlineData("0.0.0-pr.117.112", "0.6.0-beta.1")]
    [InlineData("0.7.0-beta.9", "0.7.0-beta.9.1")]
    [InlineData("0.7.0-beta.9", "0.7.0-rc")] // numbers before words
    public void OrdersVersions(string older, string newer)
    {
        var a = ReleaseVersion.Parse(older)!;
        var b = ReleaseVersion.Parse(newer)!;

        Assert.True(a.CompareTo(b) < 0);
        Assert.True(b.CompareTo(a) > 0);
    }

    [Fact]
    public void BuildMetadataAndALeadingVAreIgnored()
    {
        Assert.Equal(0, ReleaseVersion.Parse("v1.2.3-beta.4")!.CompareTo(ReleaseVersion.Parse("1.2.3-beta.4+abc123")!));
        Assert.Equal("1.2.3-beta.4", ReleaseVersion.Parse("v1.2.3-beta.4+abc")!.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("1.2")]
    [InlineData("1.2.x")]
    [InlineData("1.2.3-")]
    public void OtherTextIsNoVersion(string text)
    {
        Assert.Null(ReleaseVersion.Parse(text));
    }
}
