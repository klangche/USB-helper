using UsbHelper.App.Licensing;
using UsbHelper.App.Resources;
using UsbHelper.App.ViewModels;
using UsbHelper.Core.Diagnostics;
using static UsbHelper.App.Tests.Licensing.UnlockCodeTests;

namespace UsbHelper.App.Tests.Licensing;

/// <summary>The unlock popup (#234, #238): email, code, Unlock and Remove code; and the top bar text.</summary>
public sealed class UnlockViewModelTests
{
    private static UnlockViewModel Model(IReadOnlyDictionary<int, byte[]> keys, bool freeUnlock = false) =>
        new(new CodeEntitlements(keys, new SettingsViewModel(), new EventLog(), freeUnlock));

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

    [Fact]
    public void AWrongPairKeepsNothingAndThePopupStaysOpen()
    {
        var settings = new SettingsViewModel();
        var model = new UnlockViewModel(new CodeEntitlements(TestKeys, settings, new EventLog()));
        var closed = 0;
        model.Unlocked += (_, _) => closed++;
        model.Email = "other@example.com";
        model.Code = TestCode;

        model.UnlockCommand.Execute(null);

        Assert.Equal(0, closed);
        Assert.Null(settings.LicenceEmail);
        Assert.Null(settings.LicenceCode);
        Assert.Equal("This code does not match this email", model.Message);
    }

    [Fact]
    public void AValidPairUnlocksAndClosesThePopup()
    {
        var model = Model(TestKeys);
        var closed = 0;
        model.Unlocked += (_, _) => closed++;
        model.Email = TestEmail;
        model.Code = TestCode;

        model.UnlockCommand.Execute(null);

        Assert.Equal(1, closed);
        Assert.True(model.IsUnlocked);
        Assert.Equal("Unlocked to test@example.com", model.Message);
    }

    [Fact]
    public void RemoveCodeLocksAgain()
    {
        var model = Model(TestKeys);
        model.Email = TestEmail;
        model.Code = TestCode;
        model.UnlockCommand.Execute(null);

        model.RemoveCommand.Execute(null);

        Assert.False(model.IsUnlocked);
        Assert.False(model.FeaturesUnlocked);
        Assert.Null(model.UnlockedText);
    }

    [Fact]
    public void OpeningThePopupAgainForgetsAnEarlierWrongTry()
    {
        var model = Model(TestKeys);
        model.Email = "other@example.com";
        model.Code = TestCode;
        model.UnlockCommand.Execute(null);

        model.Reset();

        Assert.Equal("", model.Email);
        Assert.Equal("", model.Code);
        Assert.Null(model.Message);
    }

    [Fact]
    public void TheTopBarOffersUnlockWhileLocked()
    {
        var model = Model(TestKeys);

        Assert.False(model.FeaturesUnlocked);
        Assert.Null(model.UnlockedText);
    }

    [Fact]
    public void TheTopBarSaysFreeBetaInAFreeBuildWithoutACode()
    {
        var model = Model(TestKeys, freeUnlock: true);

        Assert.True(model.FeaturesUnlocked);
        Assert.False(model.IsUnlocked); // no personal code: the popup still takes one
        Assert.Equal("Unlocked (free beta)", model.UnlockedText);
    }

    [Fact]
    public void TheTopBarNamesTheEmailOnceUnlockedByCode()
    {
        foreach (var free in new[] { false, true })
        {
            var model = Model(TestKeys, free);
            var changed = new List<string?>();
            model.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            model.Email = TestEmail;
            model.Code = TestCode;

            model.UnlockCommand.Execute(null);

            Assert.True(model.FeaturesUnlocked);
            Assert.Equal("Unlocked to test@example.com", model.UnlockedText);
            Assert.Contains(nameof(UnlockViewModel.UnlockedText), changed);
            Assert.Contains(nameof(UnlockViewModel.FeaturesUnlocked), changed);
        }
    }
}
