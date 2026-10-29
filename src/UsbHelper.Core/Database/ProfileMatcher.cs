using UsbHelper.Core.Detection;

namespace UsbHelper.Core.Database;

/// <summary>
/// Finds the computer profile for the computer the app runs on. Only an exact match of both manufacturer and
/// model counts (ignoring case and surrounding spaces); otherwise the user chooses. Never guesses.
/// </summary>
public static class ProfileMatcher
{
    /// <summary>The single matching profile, or null when none or more than one matches.</summary>
    public static ResolvedProfile? Match(HardwareDatabase database, ComputerIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(identity.Manufacturer) || string.IsNullOrWhiteSpace(identity.Model))
        {
            return null;
        }
        var matches = database.Computers.Values
            .Where(profile => Same(profile.Find("manufacturer")?.Scalar, identity.Manufacturer)
                && Same(profile.Find("model")?.Scalar, identity.Model))
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private static bool Same(object? profileValue, string reported) =>
        profileValue is string text && string.Equals(text.Trim(), reported.Trim(), StringComparison.OrdinalIgnoreCase);
}
