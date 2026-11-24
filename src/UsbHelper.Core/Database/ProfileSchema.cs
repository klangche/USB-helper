using System.Globalization;

namespace UsbHelper.Core.Database;

/// <summary>
/// Allowed fields of each kind of database file and their types. Any field except <c>verification.status</c> may be
/// <c>null</c> (unknown).
/// </summary>
internal static class ProfileSchema
{
    internal enum FieldType { String, Integer, Boolean, Date, Status, StringList, Mapping, Hubs }

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
        ["usb4.max_router_depth"] = FieldType.Integer, // A reference, loaded and checked: a guide judges routers with its own rule on a measure that counts usb4_router (#332).
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

    /// <summary>v4 <c>base.yaml</c> (#327): the v3 facts, the path elements and the device kinds. "*" stands for any name.</summary>
    public static readonly IReadOnlyDictionary<string, FieldType> BaseFieldsV4 = With(BaseFields.ToDictionary(), new()
    {
        ["path_elements"] = FieldType.Mapping,
        ["path_elements.*"] = FieldType.Mapping,
        ["path_elements.*.found_by"] = FieldType.String, // How the app finds it, or that it is not measured yet.
        ["device_kinds"] = FieldType.Mapping,
        ["device_kinds.*"] = FieldType.Mapping,
        ["device_kinds.*.usb_class"] = FieldType.Integer, // The USB interface class, for example 0x0E (14) for a camera.
        // The standard counts a readable philosophy names (#386): chain, tiers, room, each with what it adds.
        ["counts"] = FieldType.Mapping,
        ["counts.*"] = FieldType.Mapping,
        ["counts.*.explain"] = FieldType.String,
        ["counts.*.adds"] = FieldType.String,
    });

    /// <summary>
    /// v4 platform files (#327): <c>computer_hubs</c> instead of <c>built_in_hub_levels</c>; and <c>displays.mst</c> for the
    /// operating system and per chip (#367), a fact about the computer that moves here from the philosophy files.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, FieldType> PlatformFieldsV4 = With(V4(PlatformFields, "chips.*."), new()
    {
        ["displays"] = FieldType.Mapping,
        ["displays.mst"] = FieldType.Boolean,
        ["chips.*.displays"] = FieldType.Mapping,
        ["chips.*.displays.mst"] = FieldType.Boolean,
    });

    /// <summary>v4 computer files (#327): <c>computer_hubs</c> instead of <c>built_in_hub_levels</c>.</summary>
    public static readonly IReadOnlyDictionary<string, FieldType> ComputerFieldsV4 = V4(ComputerFields, "");

    // The computer's own hubs: a number, null, or one per bus, { usb2: n, usb3: n }.
    private static Dictionary<string, FieldType> V4(IReadOnlyDictionary<string, FieldType> v3, string prefix)
    {
        var fields = v3.Where(field => field.Key != $"{prefix}built_in_hub_levels").ToDictionary();
        fields[$"{prefix}computer_hubs"] = FieldType.Hubs;
        return fields;
    }

    /// <summary>The header of a guide (<c>guides/&lt;guide&gt;.yaml</c>); the rest of a guide is checked by <see cref="GuideSchema"/>.</summary>
    public static readonly IReadOnlyDictionary<string, FieldType> GuideHeaderFields = With(Common, new()
    {
        ["name"] = FieldType.String,
        ["selectable"] = FieldType.Boolean,
        ["proav"] = FieldType.Integer,
        ["count"] = FieldType.StringList, // What the guide counts: chain, tiers, hubs (#310); checked by GuideSchema.
        ["link"] = FieldType.String, // The guide's page, an http or https address (#310).
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
        if (parts.Length >= 2 && parts[0] is "ports" or "chips" or "path_elements" or "device_kinds" or "counts")
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
        FieldType.Hubs when value.Kind == ValueKind.Integer => (long)value.Scalar! < 0 ? "must not be negative." : null,
        FieldType.Hubs when value.Kind != ValueKind.Mapping
            || value.Fields.Any(bus => bus.Key is not ("usb2" or "usb3") || bus.Value.Kind is not (ValueKind.Integer or ValueKind.Null)
                || bus.Value.Scalar is < 0L)
            => "expected a whole number, null, or { usb2: n, usb3: n }.",
        _ => null,
    };
}
