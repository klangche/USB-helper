namespace UsbHelper.App.Licensing;

/// <summary>The extra features a code unlocks (#234); the values are the bits of the code's features nibble.</summary>
[Flags]
public enum Feature
{
    /// <summary>Company branding and the PDF report (#227).</summary>
    Pro = 1,
}

/// <summary>
/// What is unlocked (#234). Every feature check goes through this. Only <see cref="CodeEntitlements"/> exists now; a
/// Microsoft Store or App Store purchase can implement it later with no change to feature code.
/// </summary>
public interface IEntitlements
{
    bool IsUnlocked(Feature feature);

    /// <summary>Raised when features are unlocked or locked again.</summary>
    event EventHandler? Changed;

    /// <summary>The email the features are unlocked for; null while locked.</summary>
    string? LicensedTo { get; }
}
