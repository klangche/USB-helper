using UsbHelper.Detection.MacOS;

namespace UsbHelper.Detection.MacOS.Tests;

public class MacComputerIdentityTests
{
    [Fact]
    public void AppleSiliconMacMatchesItsComputerProfileId()
    {
        var identity = MacComputerIdentity.Build("MacBookPro18,2", "Apple M1 Max", appleSilicon: true, "Mac");

        Assert.Equal("Apple Inc.", identity.Manufacturer);
        Assert.Equal("MacBookPro18,2", identity.Model);
        Assert.Equal("Apple M1 Max", identity.Chip);
        Assert.Equal("macos-apple-silicon", identity.Platform);
    }

    [Fact]
    public void IntelMacUsesTheIntelPlatform()
    {
        Assert.Equal("macos-intel", MacComputerIdentity.Build("MacBookPro15,1", "Intel(R) Core(TM) i7", appleSilicon: false, null).Platform);
    }

    [Fact]
    public void BlankValuesAreUnknown()
    {
        var identity = MacComputerIdentity.Build(" ", null, appleSilicon: false, null);

        Assert.Null(identity.Model);
        Assert.Null(identity.Chip);
    }

    [Fact]
    public void ReadsThisMacWhenRunningOnOne()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }
        var identity = MacComputerIdentity.Read();

        Assert.False(string.IsNullOrWhiteSpace(identity.Model));
        Assert.Matches(@"^[A-Za-z]+\d+,\d+$", identity.Model);
    }
}
