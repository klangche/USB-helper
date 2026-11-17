namespace UsbHelper.Core.Database;

/// <summary>
/// The shape every guide folder must have (owner decisions 2026-11-17, #285): <c>guide.yaml</c> with the header,
/// <c>all.yaml</c> with one limits block, and one file per platform of <c>platforms/*.yaml</c> with a limits block per
/// chip. A returned folder with a missing or extra file or key is rejected, naming file, line and key.
/// </summary>
internal static class GuideSchema
{
    public const string HeaderFile = "guide.yaml";
    public const string AllFile = "all.yaml";

    private static readonly string[] Header = ["name", "selectable", "notes", "sources", "verification"];
    private static readonly string[] Buses = ["usb2", "usb3", "usb4"];
    private static readonly string[] Kinds = ["hubs", "tiers", "hops"];
    private static readonly string[] Values = ["recommended", "max"];
    private static readonly string[] BlockKeys = [.. Buses, "camera_audio", "mst", "exceeded_note"];

    public static void ValidateHeader(ProfileValue root, List<DatabaseError> errors)
    {
        ProfileSchema.Validate(
            ProfileValue.CreateMapping(root.Fields.Where(field => Header.Contains(field.Key)).ToDictionary(), root.File, root.Line),
            ProfileSchema.GuideHeaderFields, errors);
        foreach (var (key, value) in root.Fields.Where(field => !Header.Contains(field.Key)))
        {
            errors.Add(new DatabaseError(root.File, value.Line, $"Unknown key '{key}': every guide has the files and keys of guides/usb-helper/."));
        }
    }

    /// <param name="isDefault">USB-Helper's guide: the fallback, so every number and <c>mst</c> must be filled in.</param>
    /// <param name="maxTiers"><c>usb.max_tiers</c> from <c>base.yaml</c>; no tier value may be above it.</param>
    public static void Block(ProfileValue block, string path, bool isDefault, long? maxTiers, List<DatabaseError> errors) =>
        new Checker(block.File, isDefault, maxTiers, errors).Block(block, path);

    /// <summary>A platform file of a guide: one limits block per chip, exactly the platform's chips.</summary>
    public static void Platform(ProfileValue root, IReadOnlyList<string> chips, bool isDefault, long? maxTiers, List<DatabaseError> errors)
    {
        var check = new Checker(root.File, isDefault, maxTiers, errors);
        check.Keys(root, "", chips);
        foreach (var chip in chips)
        {
            if (check.Mapping(root.Fields.GetValueOrDefault(chip), chip) is { } block)
            {
                check.Block(block, chip);
            }
        }
    }

    private sealed class Checker(string file, bool isDefault, long? maxTiers, List<DatabaseError> errors)
    {
        // Missing keys are reported on the line of the mapping that should hold them; extra keys on their own line.
        public void Keys(ProfileValue mapping, string prefix, IReadOnlyCollection<string> keys)
        {
            foreach (var (key, value) in mapping.Fields.Where(field => !keys.Contains(field.Key)))
            {
                errors.Add(new DatabaseError(file, value.Line, $"Unknown key '{prefix}{key}': every guide has the files and keys of guides/usb-helper/."));
            }
            foreach (var key in keys.Where(key => !mapping.Fields.ContainsKey(key)))
            {
                errors.Add(new DatabaseError(file, mapping.Line, $"Missing key '{prefix}{key}': every guide has the files and keys of guides/usb-helper/."));
            }
        }

        public ProfileValue? Mapping(ProfileValue? value, string path)
        {
            if (value == null)
            {
                return null; // Reported as missing by Keys.
            }
            if (value.Kind != ValueKind.Mapping)
            {
                errors.Add(new DatabaseError(file, value.Line, $"Key '{path}': expected a mapping."));
                return null;
            }
            return value;
        }

        public void Block(ProfileValue block, string path)
        {
            var prefix = path.Length == 0 ? "" : path + ".";
            Keys(block, prefix, BlockKeys);
            foreach (var bus in Buses)
            {
                if (Mapping(block.Fields.GetValueOrDefault(bus), prefix + bus) is { } limits)
                {
                    Keys(limits, $"{prefix}{bus}.", Kinds);
                    foreach (var kind in Kinds)
                    {
                        Pair(limits.Fields.GetValueOrDefault(kind), $"{prefix}{bus}.{kind}", kind == "tiers");
                    }
                }
            }
            if (Mapping(block.Fields.GetValueOrDefault("camera_audio"), prefix + "camera_audio") is { } camera)
            {
                Keys(camera, prefix + "camera_audio.", ["tiers", "hops"]);
                Pair(camera.Fields.GetValueOrDefault("tiers"), prefix + "camera_audio.tiers", tiers: true);
                Pair(camera.Fields.GetValueOrDefault("hops"), prefix + "camera_audio.hops", tiers: false);
            }
            Mst(block.Fields.GetValueOrDefault("mst"), prefix + "mst");
            if (block.Fields.GetValueOrDefault("exceeded_note") is { Kind: not ValueKind.Null } note)
            {
                if (note.Scalar is not string text)
                {
                    errors.Add(new DatabaseError(file, note.Line, $"Key '{prefix}exceeded_note': expected text."));
                }
                else if (text.Length > Guide.ExceededNoteMaxLength)
                {
                    errors.Add(new DatabaseError(file, note.Line,
                        $"Key '{prefix}exceeded_note' has {text.Length} characters; at most {Guide.ExceededNoteMaxLength}."));
                }
            }
        }

        private void Mst(ProfileValue? value, string path)
        {
            if (value == null || Empty(value, path))
            {
                return;
            }
            if (value.Scalar is not (0L or 1L))
            {
                errors.Add(new DatabaseError(file, value.Line, $"Key '{path}': expected 1 (yes) or 0 (no), or nothing for USB-Helper's value."));
            }
        }

        private void Pair(ProfileValue? value, string path, bool tiers)
        {
            if (Mapping(value, path) is not { } pair)
            {
                return;
            }
            Keys(pair, path + ".", Values);
            foreach (var name in Values)
            {
                Number(pair.Fields.GetValueOrDefault(name), $"{path}.{name}", tiers);
            }
        }

        private void Number(ProfileValue? value, string path, bool tiers)
        {
            if (value == null || Empty(value, path) || value.Scalar is Guide.None)
            {
                return;
            }
            if (value.Scalar is not long number || number < 0)
            {
                errors.Add(new DatabaseError(file, value.Line, $"Key '{path}': expected a whole number, none, or nothing for USB-Helper's value."));
            }
            else if (tiers && number > maxTiers)
            {
                errors.Add(new DatabaseError(file, value.Line, $"Key '{path}' ({number}) must not be above usb.max_tiers ({maxTiers}) in base.yaml."));
            }
        }

        // An empty value falls back to USB-Helper's, so USB-Helper's own files must fill every one in.
        private bool Empty(ProfileValue value, string path)
        {
            if (value.Kind != ValueKind.Null)
            {
                return false;
            }
            if (isDefault)
            {
                errors.Add(new DatabaseError(file, value.Line, $"Key '{path}' must be filled in: guides/usb-helper/ is the fallback for every other guide (none states no limit)."));
            }
            return true;
        }
    }
}
