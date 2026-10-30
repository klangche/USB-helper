using System.Globalization;

namespace UsbHelper.Core.Database;

/// <summary>Allowed fields of a profile and their types. Any field except <c>verification.status</c> may be <c>null</c> (unknown).</summary>
internal static class ProfileSchema
{
    private enum FieldType { String, Integer, Boolean, Date, Status, StringList, Mapping }

    // "*" stands for any port name.
    private static readonly Dictionary<string, FieldType> Fields = new()
    {
        ["platform"] = FieldType.String,
        ["architecture"] = FieldType.String,
        ["manufacturer"] = FieldType.String,
        ["model"] = FieldType.String,
        ["chip"] = FieldType.String,
        ["limits"] = FieldType.Mapping,
        ["limits.max_hubs"] = FieldType.Integer,
        ["limits.max_tiers"] = FieldType.Integer,
        ["limits.max_hops"] = FieldType.Integer,
        ["limits.recommended_hops"] = FieldType.Integer, // Deeper still works up to max_hops, but less reliably.
        ["displays"] = FieldType.Mapping,
        ["displays.max_external"] = FieldType.Integer,
        ["displays.restrictions"] = FieldType.StringList, // For example ports that cannot drive displays at the same time.
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
        ["notes"] = FieldType.StringList,
        ["exceptions"] = FieldType.StringList,
        ["sources"] = FieldType.StringList,
        ["verification"] = FieldType.Mapping,
        ["verification.status"] = FieldType.Status,
        ["verification.last_checked"] = FieldType.Date,
    };

    private static readonly string[] ComputerOnlyFields = ["manufacturer", "model", "chip", "ports"];

    public static readonly string[] VerificationStatuses = ["verified", "partial", "unverified"];

    /// <summary>Checks every field of <paramref name="mapping"/> except those in <paramref name="skip"/>.</summary>
    public static void Validate(ProfileValue mapping, IReadOnlySet<string> skip, List<DatabaseError> errors) =>
        ValidateMapping(mapping, "", skip, errors);

    /// <summary>Rejects fields that only computer profiles may set, such as port layouts.</summary>
    public static void RejectComputerOnlyFields(ProfileValue mapping, string profileKind, List<DatabaseError> errors)
    {
        foreach (var field in ComputerOnlyFields.Where(mapping.Fields.ContainsKey))
        {
            errors.Add(new DatabaseError(mapping.File, mapping.Fields[field].Line, $"Field '{field}' belongs in computer profiles, not {profileKind}."));
        }
    }

    private static void ValidateMapping(ProfileValue mapping, string prefix, IReadOnlySet<string> skip, List<DatabaseError> errors)
    {
        foreach (var (key, value) in mapping.Fields)
        {
            var path = prefix + key;
            if (skip.Contains(path))
            {
                continue;
            }
            if (!Fields.TryGetValue(SchemaPath(path), out var type))
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
                ValidateMapping(value, path + ".", skip, errors);
            }
        }
    }

    private static string SchemaPath(string path)
    {
        var parts = path.Split('.');
        if (parts.Length >= 2 && parts[0] == "ports")
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
