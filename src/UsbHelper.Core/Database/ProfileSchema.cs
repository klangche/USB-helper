using System.Globalization;

namespace UsbHelper.Core.Database;

/// <summary>
/// Allowed fields of each kind of database file and their types. Any field except <c>verification.status</c> may be
/// <c>null</c> (unknown).
/// </summary>
internal static class ProfileSchema
{
    internal enum FieldType { String, Integer, Boolean, Date, Status, StringList, Mapping }

    // The fields every file may have.
    private static readonly Dictionary<string, FieldType> Common = new()
    {
        ["notes"] = FieldType.StringList,
        ["sources"] = FieldType.StringList,
        ["verification"] = FieldType.Mapping,
        ["verification.status"] = FieldType.Status,
        ["verification.last_checked"] = FieldType.Date,
    };

    private static readonly Dictionary<string, FieldType> Displays = new()
    {
        ["displays"] = FieldType.Mapping,
        ["displays.max_external"] = FieldType.Integer,
        ["displays.restrictions"] = FieldType.StringList, // For example ports that cannot drive displays at the same time.
        ["displays.mst"] = FieldType.Boolean, // DisplayPort Multi-Stream Transport: several displays on one DP output (#144).
    };

    /// <summary><c>base.yaml</c>: facts from the USB specification. <c>schema_version</c> is checked by the loader.</summary>
    public static readonly IReadOnlyDictionary<string, FieldType> BaseFields = With(Common, new()
    {
        ["usb"] = FieldType.Mapping,
        ["usb.max_tiers"] = FieldType.Integer, // Seven tiers on a path: the host with its root hub, up to five hubs, the device.
        ["usb.max_devices_per_controller"] = FieldType.Integer, // A 7-bit device address: 127.
        ["usb4"] = FieldType.Mapping,
        ["usb4.max_router_depth"] = FieldType.Integer, // Loaded, not judged yet: USB4 links are not detected yet (#267).
    });

    /// <summary><c>platforms/*.yaml</c>: one operating system with its chips. "*" stands for any chip key.</summary>
    public static readonly IReadOnlyDictionary<string, FieldType> PlatformFields = With(Common, new()
    {
        ["name"] = FieldType.String,
        ["exceptions"] = FieldType.StringList,
        ["chips"] = FieldType.Mapping,
        ["chips.*"] = FieldType.Mapping,
        ["chips.*.name"] = FieldType.String,
        ["chips.*.architecture"] = FieldType.String,
        ["chips.*.built_in_hub_levels"] = FieldType.Integer, // Hub levels inside the computer, in front of its own port.
    });

    /// <summary><c>computers/**/*.yaml</c>: a problem machine. "*" stands for any port name.</summary>
    public static readonly IReadOnlyDictionary<string, FieldType> ComputerFields = With(Common, Displays, new()
    {
        ["platform"] = FieldType.String, // A platform file's name without .yaml.
        ["chip_family"] = FieldType.String, // One of that platform's chips.
        ["manufacturer"] = FieldType.String,
        ["model"] = FieldType.String,
        ["name"] = FieldType.String, // Human-readable, for example "MacBook Neo" for model "Mac17,5". Shown only.
        ["chip"] = FieldType.String, // The processor as the operating system reports it.
        ["built_in_hub_levels"] = FieldType.Integer,
        ["exceptions"] = FieldType.StringList,
        ["ports"] = FieldType.Mapping,
        ["ports.*"] = FieldType.Mapping,
        ["ports.*.location"] = FieldType.String,
        ["ports.*.usb"] = FieldType.String,
        ["ports.*.thunderbolt"] = FieldType.Integer, // Generation, for example 4.
        ["ports.*.usb4"] = FieldType.Boolean,
        ["ports.*.displayport_alt_mode"] = FieldType.Boolean,
        ["ports.*.displayport_mst"] = FieldType.Boolean,
        ["ports.*.native_displayport"] = FieldType.Boolean,
        ["ports.*.hdmi"] = FieldType.Boolean,
        ["ports.*.displaylink"] = FieldType.Boolean,
        ["ports.*.limitations"] = FieldType.StringList,
    });

    /// <summary>The header of a guide (<c>guides/<c>guides/&lt;guide&gt;/guide.yaml</c>); the restlt;guide<c>guides/&lt;guide&gt;/guide.yaml</c>); the restgt;.yaml</c>); the rest of a guide is checked by <see cref="GuideSchema"/>.</summary>
    public static readonly IReadOnlyDictionary<string, FieldType> GuideHeaderFields = With(Common, new()
    {
        ["name"] = FieldType.String,
        ["selectable"] = FieldType.Boolean,
    });

    public static readonly string[] VerificationStatuses = ["verified", "partial", "unverified"];

    /// <summary>Checks every field of <paramref name="mapping"/> against <paramref name="fields"/>.</summary>
    public static void Validate(ProfileValue mapping, IReadOnlyDictionary<string, FieldType> fields, List<DatabaseError> errors) =>
        ValidateMapping(mapping, "", fields, errors);

    private static Dictionary<string, FieldType> With(params Dictionary<string, FieldType>[] parts) =>
        parts.SelectMany(part => part).ToDictionary();

    private static void ValidateMapping(ProfileValue mapping, string prefix, IReadOnlyDictionary<string, FieldType> fields, List<DatabaseError> errors)
    {
        foreach (var (key, value) in mapping.Fields)
        {
            var path = prefix + key;
            if (!fields.TryGetValue(SchemaPath(path), out var type))
            {
                errors.Add(new DatabaseError(value.File, value.Line, $"Unknown field '{path}'."));
                continue;
            }
            if (value.Kind == ValueKind.Null && type != FieldType.Status)
            {
                continue;
            }
            var problem = Check(type, value);
            if (problem != null)
            {
                errors.Add(new DatabaseError(value.File, value.Line, $"Field '{path}': {problem}"));
            }
            else if (type == FieldType.Mapping)
            {
                ValidateMapping(value, path + ".", fields, errors);
            }
        }
    }

    // The second part of a port or chip path is a name the file chooses.
    private static string SchemaPath(string path)
    {
        var parts = path.Split('.');
        if (parts.Length >= 2 && parts[0] is "ports" or "chips")
        {
            parts[1] = "*";
        }
        return string.Join('.', parts);
    }

    private static string? Check(FieldType type, ProfileValue value) => type switch
    {
        FieldType.String when value.Kind != ValueKind.String => "expected text.",
        FieldType.Integer when value.Kind != ValueKind.Integer => "expected a whole number or null.",
        FieldType.Integer when (long)value.Scalar! < 0 => "must not be negative.",
        FieldType.Boolean when value.Kind != ValueKind.Boolean => "expected true, false or null.",
        FieldType.Date when value.Kind != ValueKind.String
            || !DateOnly.TryParseExact((string)value.Scalar!, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            => "expected a date as YYYY-MM-DD or null.",
        FieldType.Status when value.Kind != ValueKind.String || !VerificationStatuses.Contains((string)value.Scalar!)
            => $"expected one of {string.Join(", ", VerificationStatuses)}.",
        FieldType.StringList when value.Kind != ValueKind.List => "expected a list.",
        FieldType.StringList when value.Items.Any(item => item.Kind != ValueKind.String) => "expected a list of text items.",
        FieldType.Mapping when value.Kind != ValueKind.Mapping => "expected a mapping.",
        _ => null,
    };
}
