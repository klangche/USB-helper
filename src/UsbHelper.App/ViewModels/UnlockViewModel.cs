using System.Globalization;
using UsbHelper.App.Licensing;
using UsbHelper.App.Resources;

namespace UsbHelper.App.ViewModels;

/// <summary>
/// The unlock popup (#234, #238): the email and unlock code, Unlock and Remove code; and what the top bar and the status line show about it.
/// </summary>
public sealed class UnlockViewModel : ObservableObject
{
    private readonly CodeEntitlements entitlements;
    private string email = "";
    private string code = "";
    private string? message;

    public UnlockViewModel(CodeEntitlements entitlements)
    {
        this.entitlements = entitlements;
        UnlockCommand = new RelayCommand(Unlock);
        RemoveCommand = new RelayCommand(Remove);
        message = ShownMessage();
    }

    /// <summary>Raised when a valid pair unlocked, so the popup closes.</summary>
    public event EventHandler? Unlocked;

    public string Email
    {
        get => email;
        set => SetField(ref email, value);
    }

    public string Code
    {
        get => code;
        set => SetField(ref code, value);
    }

    /// <summary>True when unlocked by a personal code: the popup then offers Remove code.</summary>
    public bool IsUnlocked => entitlements.LicensedTo is not null;

    /// <summary>True when the extra features are unlocked, by a code or by a free-unlock build: the status line shows <see cref="UnlockedText"/> instead of the top bar's Unlock button.</summary>
    public bool FeaturesUnlocked => entitlements.IsUnlocked(Feature.Pro);

    /// <summary>The status line's "Unlocked to &lt;email&gt;" or "Unlocked (free beta)"; null while locked.</summary>
    public string? UnlockedText =>
        entitlements.LicensedTo is { } licensedTo ? string.Format(CultureInfo.CurrentCulture, Strings.UnlockedFor, licensedTo)
        : FeaturesUnlocked ? Strings.UnlockFreeBeta
        : null;

    /// <summary>Who it is unlocked for, why a code was refused, or that this build takes no codes.</summary>
    public string? Message
    {
        get => message;
        private set => SetField(ref message, value);
    }

    public RelayCommand UnlockCommand { get; }

    public RelayCommand RemoveCommand { get; }

    /// <summary>Empties the fields and forgets an earlier refusal; called each time the popup opens.</summary>
    public void Reset()
    {
        Email = "";
        Code = "";
        Message = ShownMessage();
    }

    private void Unlock()
    {
        var result = entitlements.Unlock(Email, Code);
        Message = result switch
        {
            UnlockResult.Unlocked => ShownMessage(),
            UnlockResult.NoMatch => Strings.UnlockNoMatch,
            _ => Strings.UnlockNotAvailable,
        };
        StateChanged();
        if (result == UnlockResult.Unlocked)
        {
            Unlocked?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Remove()
    {
        entitlements.Remove();
        Email = "";
        Code = "";
        Message = ShownMessage();
        StateChanged();
    }

    private void StateChanged()
    {
        OnPropertyChanged(nameof(IsUnlocked));
        OnPropertyChanged(nameof(FeaturesUnlocked));
        OnPropertyChanged(nameof(UnlockedText));
    }

    private string? ShownMessage() =>
        entitlements.LicensedTo is { } licensedTo ? string.Format(CultureInfo.CurrentCulture, Strings.UnlockedFor, licensedTo)
        : entitlements.FreeUnlock ? Strings.UnlockFreeBeta
        : entitlements.CodesAvailable ? null
        : Strings.UnlockNotAvailable;
}
