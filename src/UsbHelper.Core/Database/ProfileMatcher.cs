using UsbHelper.Core.Detection;

namespace UsbHelper.Core.Database;

/// <summary>
/// Finds the computer profile for the computer the app runs on. Only an exact match of both manufacturer and
/// model counts (ignoring case and extra spaces, as the computed id does); otherwise the user chooses. Never guesses.
/// </summary>
public static class ProfileMatcher
{
    /// <summary>The matching profile, or null when none matches.</summary>
    public static ResolvedProfile? Match(HardwareDatabase database, ComputerIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(identity.Manufacturer) || string.IsNullOrWhiteSpace(identity.Model))
        {
            return null;
        }
        return database.Computers.GetValueOrDefault(ComputerProfileId.From(identity.Manufacturer, identity.Model));
    }
}
