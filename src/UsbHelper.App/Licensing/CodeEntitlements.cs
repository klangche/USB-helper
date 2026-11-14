using UsbHelper.App.ViewModels;
using UsbHelper.Core.Diagnostics;

namespace UsbHelper.App.Licensing;

/// <summary>The outcome of <see cref="CodeEntitlements.Unlock"/>.</summary>
public enum UnlockResult
{
    Unlocked,
    NoMatch,
    /// <summary>This build has no keys, so no code unlocks.</summary>
    NotAvailable,
}

/// <summary>
/// Entitlements from an email and an unlock code (#234), kept in settings.json and checked offline at every start.
/// No network calls.
/// </summary>
public sealed class CodeEntitlements : IEntitlements
{
    private readonly IReadOnlyDictionary<int, byte[]> keys;
    private readonly SettingsViewModel settings;
    private int features;

    /// <param name="keys">The secrets by keyId: <see cref="LicenceKeys.All"/> in the app, a test secret in tests.</param>
    /// <param name="freeUnlock"><see cref="BuildInfo.FreeUnlock"/> in the app: every feature is unlocked without a code (#238).</param>
    public CodeEntitlements(IReadOnlyDictionary<int, byte[]> keys, SettingsViewModel settings, EventLog log, bool freeUnlock = false)
    {
        this.keys = keys;
        this.settings = settings;
        FreeUnlock = freeUnlock;
        if (settings.LicenceEmail is { } email && settings.LicenceCode is { } code)
        {
            features = UnlockCode.Verify(keys, email, code) ?? 0;
            // This start message leaves the email out; MainViewModel logs the licensed email separately (#230).
            log.Add(features != 0 ? "Extra features unlocked by the saved unlock code" : "The saved unlock code does not match its email in this build; extra features locked");
        }
    }

    public event EventHandler? Changed;

    /// <summary>False in a build without keys (local and fork builds).</summary>
    public bool CodesAvailable => keys.Count > 0;

    /// <summary>True in a free-unlock build (#238): every feature is unlocked, with or without a code.</summary>
    public bool FreeUnlock { get; }

    /// <summary>Only a personal code licenses an email; a free-unlock build alone does not.</summary>
    public string? LicensedTo => features != 0 ? settings.LicenceEmail : null;

    public bool IsUnlocked(Feature feature) => FreeUnlock || (features & (int)feature) == (int)feature;

    /// <summary>Unlocks and saves the pair when the code matches the email; a wrong pair changes and saves nothing.</summary>
    public UnlockResult Unlock(string email, string code)
    {
        if (!CodesAvailable)
        {
            return UnlockResult.NotAvailable;
        }
        if (UnlockCode.Verify(keys, email, code) is not { } unlocked)
        {
            return UnlockResult.NoMatch;
        }
        features = unlocked;
        settings.Licence(email.Trim(), code.Trim());
        Changed?.Invoke(this, EventArgs.Empty);
        return UnlockResult.Unlocked;
    }

    /// <summary>Locks the features again (none in a <see cref="FreeUnlock"/> build) and forgets the saved pair.</summary>
    public void Remove()
    {
        features = 0;
        settings.Licence(null, null);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
