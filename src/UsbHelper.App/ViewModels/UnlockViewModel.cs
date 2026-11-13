using System.Globalization;
using UsbHelper.App.Licensing;
using UsbHelper.App.Resources;

namespace UsbHelper.App.ViewModels;

/// <summary>Settings > Support the project (#234): the email and unlock code, Unlock and Remove.</summary>
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

    public bool IsUnlocked => entitlements.LicensedTo is not null;

    /// <summary>Who it is unlocked for, why a code was refused, or that this build takes no codes.</summary>
    public string? Message
    {
        get => message;
        private set => SetField(ref message, value);
    }

    public RelayCommand UnlockCommand { get; }

    public RelayCommand RemoveCommand { get; }

    private void Unlock()
    {
        Message = entitlements.Unlock(Email, Code) switch
        {
            UnlockResult.Unlocked => ShownMessage(),
            UnlockResult.NoMatch => Strings.UnlockNoMatch,
            _ => Strings.UnlockNotAvailable,
        };
        OnPropertyChanged(nameof(IsUnlocked));
    }

    private void Remove()
    {
        entitlements.Remove();
        Email = "";
        Code = "";
        Message = ShownMessage();
        OnPropertyChanged(nameof(IsUnlocked));
    }

    private string? ShownMessage() =>
        entitlements.LicensedTo is { } licensedTo ? string.Format(CultureInfo.CurrentCulture, Strings.UnlockedFor, licensedTo)
        : entitlements.CodesAvailable ? null
        : Strings.UnlockNotAvailable;
}
