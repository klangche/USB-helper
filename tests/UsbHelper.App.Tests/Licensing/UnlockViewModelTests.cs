using UsbHelper.App.Licensing;
using UsbHelper.App.Resources;
using UsbHelper.App.ViewModels;
using UsbHelper.Core.Diagnostics;
using static UsbHelper.App.Tests.Licensing.UnlockCodeTests;

namespace UsbHelper.App.Tests.Licensing;

/// <summary>Settings > Support the project (#234): email, code, Unlock and Remove.</summary>
public sealed class UnlockViewModelTests
{
    private static UnlockViewModel Model(IReadOnlyDictionary<int, byte[]> keys) =>
        new(new CodeEntitlements(keys, new SettingsViewModel(), new EventLog()));

    [Fact]
    public void AWrongPairSaysSo()
    {
        var model = Model(TestKeys);
        model.Email = "other@example.com";
        model.Code = TestCode;

        model.UnlockCommand.Execute(null);

        Assert.False(model.IsUnlocked);
        Assert.Equal(Strings.UnlockNoMatch, model.Message);
    }

    [Fact]
    public void AValidPairShowsWhoItIsFor()
    {
        var model = Model(TestKeys);
        model.Email = TestEmail;
        model.Code = TestCode;

        model.UnlockCommand.Execute(null);

        Assert.True(model.IsUnlocked);
        Assert.Equal(string.Format(Strings.UnlockedFor, TestEmail), model.Message);

        model.RemoveCommand.Execute(null);

        Assert.False(model.IsUnlocked);
        Assert.Null(model.Message);
    }

    [Fact]
    public void ABuildWithoutKeysSaysSo()
    {
        var model = Model(new Dictionary<int, byte[]>());
        Assert.Equal(Strings.UnlockNotAvailable, model.Message); // before any try
        model.Email = TestEmail;
        model.Code = TestCode;

        model.UnlockCommand.Execute(null);

        Assert.False(model.IsUnlocked);
        Assert.Equal(Strings.UnlockNotAvailable, model.Message);
    }
}
