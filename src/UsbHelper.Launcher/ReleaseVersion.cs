using System.Globalization;

namespace UsbHelper.Launcher;

/// <summary>
/// A release version as the release workflow makes them (1.2.3, 1.2.3-beta.4, 0.0.0-pr.117.112), ordered as semantic
/// versions: a release comes after its pre-releases, number parts compare as numbers, numbers before words (#111).
/// </summary>
public sealed class ReleaseVersion : IComparable<ReleaseVersion>
{
    private readonly int[] core;
    private readonly string[] prerelease;

    private ReleaseVersion(int[] core, string[] prerelease)
    {
        this.core = core;
        this.prerelease = prerelease;
    }

    /// <summary>The version in a tag or version text ("v" and "+build" are dropped), or null if it is none.</summary>
    public static ReleaseVersion? Parse(string text)
    {
        text = text.Split('+')[0];
        if (text.StartsWith('v'))
        {
            text = text[1..];
        }
        var dash = text.IndexOf('-');
        var parts = (dash < 0 ? text : text[..dash]).Split('.');
        var prerelease = dash < 0 ? [] : text[(dash + 1)..].Split('.');
        var core = new int[3];
        if (parts.Length != 3 || prerelease.Any(string.IsNullOrEmpty))
        {
            return null;
        }
        for (var i = 0; i < 3; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out core[i]))
            {
                return null;
            }
        }
        return new ReleaseVersion(core, prerelease);
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null)
        {
            return 1;
        }
        for (var i = 0; i < 3; i++)
        {
            if (core[i] != other.core[i])
            {
                return core[i].CompareTo(other.core[i]);
            }
        }
        if (prerelease.Length == 0 || other.prerelease.Length == 0)
        {
            return other.prerelease.Length.CompareTo(prerelease.Length); // no pre-release is the newer
        }
        for (var i = 0; i < Math.Min(prerelease.Length, other.prerelease.Length); i++)
        {
            var compared = ComparePart(prerelease[i], other.prerelease[i]);
            if (compared != 0)
            {
                return compared;
            }
        }
        return prerelease.Length.CompareTo(other.prerelease.Length);
    }

    private static int ComparePart(string a, string b)
    {
        var aNumber = long.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out var x);
        var bNumber = long.TryParse(b, NumberStyles.None, CultureInfo.InvariantCulture, out var y);
        return (aNumber, bNumber) switch
        {
            (true, true) => x.CompareTo(y),
            (true, false) => -1,
            (false, true) => 1,
            _ => string.CompareOrdinal(a, b),
        };
    }

    public override string ToString() =>
        string.Join('.', core) + (prerelease.Length == 0 ? "" : "-" + string.Join('.', prerelease));
}
