using UsbHelper.App.Licensing;

namespace UsbHelper.App.Tests.Licensing;

/// <summary>Entitlements a test locks and unlocks by hand; each change raises <see cref="Changed"/>.</summary>
internal sealed class FakeEntitlements : IEntitlements
{
    private bool pro;

    public event EventHandler? Changed;

    public string? LicensedTo => pro ? "test@example.com" : null;

    public bool IsUnlocked(Feature feature) => pro && feature == Feature.Pro;

    public void SetPro(bool unlocked)
    {
        pro = unlocked;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
