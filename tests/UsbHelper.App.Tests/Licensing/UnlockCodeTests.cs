using UsbHelper.App.Licensing;

namespace UsbHelper.App.Tests.Licensing;

/// <summary>The short unlock code (#234): an HMAC of the email, checked offline against the build's keys.</summary>
public sealed class UnlockCodeTests
{
    // The test secret: bytes 0 to 31. Never a real key.
    internal static readonly byte[] TestSecret = [.. Enumerable.Range(0, 32).Select(i => (byte)i)];

    internal static readonly Dictionary<int, byte[]> TestKeys = new() { [0] = TestSecret };

    // The fixed test vector: test secret, keyId 0, features 1 (pro). The keygen repo tests against the same vector.
    internal const string TestEmail = "test@example.com";
    internal const string TestCode = "UH-04DV7-ZX63W-K99JZ-9QDA0";

    [Fact]
    public void TheTestVectorIsMadeAndAccepted()
    {
        Assert.Equal(TestCode, UnlockCode.Make(TestSecret, 0, 1, TestEmail));
        Assert.Equal(1, UnlockCode.Verify(TestKeys, TestEmail, TestCode));
    }

    [Fact]
    public void TheKeyIdIsInTheCode()
    {
        var keys = new Dictionary<int, byte[]> { [2] = TestSecret };

        Assert.Equal("UH-47F9Z-8BG98-VR26E-8W7TG", UnlockCode.Make(TestSecret, 2, 1, TestEmail));
        Assert.Equal(1, UnlockCode.Verify(keys, TestEmail, "UH-47F9Z-8BG98-VR26E-8W7TG"));
        Assert.Null(UnlockCode.Verify(TestKeys, TestEmail, "UH-47F9Z-8BG98-VR26E-8W7TG")); // keyId 2 is not in this build
    }

    [Fact]
    public void TheEmailIsTrimmedAndLowerCase()
    {
        var code = UnlockCode.Make(TestSecret, 0, 1, "a@b.se");

        Assert.Equal(code, UnlockCode.Make(TestSecret, 0, 1, "A@B.se "));
        Assert.Equal(1, UnlockCode.Verify(TestKeys, "A@B.se ", code));
    }

    [Theory]
    [InlineData("other@example.com", TestCode)] // another email
    [InlineData(TestEmail, "UH-04DV7-ZX63W-K99JZ-9QDB0")] // one changed character
    [InlineData(TestEmail, "UH-44DV7-ZX63W-K99JZ-9QDA0")] // keyId 2, unknown in this build
    [InlineData(TestEmail, "UH-04DV7-ZX63W-K99JZ-9QDA")] // too short
    [InlineData(TestEmail, "UH-04DV7-ZX63W-K99JZ-9QDA00")] // too long
    [InlineData(TestEmail, "UH-04DV7-ZX63W-K99JZ-9QDAU")] // not in the alphabet
    [InlineData(TestEmail, "UH-04DV7-ZX63W-K99JZ-9QDA8")] // padding bits set
    [InlineData(TestEmail, "hello")]
    [InlineData(TestEmail, "")]
    [InlineData("", TestCode)] // the email is mandatory
    [InlineData(" ", TestCode)]
    public void WrongPairsAreRefused(string email, string code)
    {
        Assert.Null(UnlockCode.Verify(TestKeys, email, code));
    }

    [Theory]
    [InlineData("uh-04dv7-zx63w-k99jz-9qda0")] // lower case
    [InlineData(" UH 04DV7 ZX63W K99JZ 9QDA0 ")] // spaces
    [InlineData("UH04DV7ZX63WK99JZ9QDA0")] // no dashes
    [InlineData("04DV7-ZX63W-K99JZ-9QDA0")] // no prefix
    [InlineData("UH-O4DV7-ZX63W-K99JZ-9QDAO")] // O for 0
    public void TheCodeIsReadForgivingly(string code)
    {
        Assert.Equal(1, UnlockCode.Verify(TestKeys, TestEmail, code));
    }

    [Theory]
    [InlineData("UH-07I6M-39SS6-MAEZC-SDRZ0")] // I for 1
    [InlineData("UH-07L6M-39SS6-MAEZC-SDRZ0")] // L for 1
    [InlineData("uh-07l6m-39ss6-maezc-sdrzo")]
    public void LookAlikeLettersAreRead(string code)
    {
        Assert.Equal("UH-0716M-39SS6-MAEZC-SDRZ0", UnlockCode.Make(TestSecret, 0, 1, "test2@example.com"));
        Assert.Equal(1, UnlockCode.Verify(TestKeys, "test2@example.com", code));
    }

    [Fact]
    public void ABuildWithoutKeysRefusesEveryCode()
    {
        Assert.Null(UnlockCode.Verify(new Dictionary<int, byte[]>(), TestEmail, TestCode));
    }
}
