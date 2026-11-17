namespace UsbHelper.Core.Database;

/// <summary>
/// The shape every guide file must have (owner decisions 2026-11-17, #285): a header, global <c>limits</c>, and
/// <c>platforms</c> with exactly the platforms of <c>platforms/*.yaml</c>, each with <c>mst</c>, <c>limits</c> and exactly
/// its chips, each with <c>mst</c> and <c>limits</c>. A limits block holds any of <c>hubs</c>, <c>tiers</c>,
/// <c>buses</c>, <c>camera_audio</c> and <c>exceeded_note</c>; USB-Helper's global block holds all of them, filled in.
/// A file with a missing or extra platform, chip or key is rejected, naming file, line and key.
/// </summary>
internal static class GuideSchema
{
    private static readonly string[] Header = ["name", "selectable", "notes", "sources", "verification"];
    private static readonly string[] Sections = ["limits", "platforms"];
    private static readonly string[] Buses = ["usb2", "usb3", "usb4"];
    private static readonly string[] Kinds = ["hubs", "tiers"];
    private static readonly string[] Values = ["recommended", "max"];
    private static readonly string[] BlockKeys = [.. Kinds, "buses", "camera_audio", "exceeded_note"];

    /// <param name="chips">Each platform file's chips, by platform.</param>
    /// <param name="isDefault">USB-Helper's guide: the fallback, so its global limits and every <c>mst</c> must be filled in.</param>
    /// <param name="maxTiers"><c>usb.max_tiers</c> from <c>base.yaml</c>; no tier value may be above it.</param>
    public static void Validate(
        ProfileValue root, IReadOnlyDictionary<string, List<string>> chips, bool isDefault, long? maxTiers, List<DatabaseError> errors)
    {
        ProfileSchema.Validate(
            ProfileValue.CreateMapping(root.Fields.Where(field => Header.Contains(field.Key)).ToDictionary(), root.File, root.Line),
            ProfileSchema.GuideHeaderFields, errors);
        var check = new Checker(root.File, isDefault, maxTiers, errors);
        check.Keys(root, "", [.. Header, .. Sections], required: false);
        check.Keys(ProfileValue.CreateMapping(root.Fields.Where(field => Sections.Contains(field.Key)).ToDictionary(), root.File, root.Line),
            "", Sections, required: true);
        if (check.Mapping(root.Fields.GetValueOrDefault("limits"), "limits") is { } limits)
        {
            check.Block(limits, "limits", all: isDefault);
        }
        if (check.Mapping(root.Fields.GetValueOrDefault("platforms"), "platforms") is not { } platforms)
        {
            return;
        }
        check.Keys(platforms, "platforms.", [.. chips.Keys], required: true);
        foreach (var (platform, platformChips) in chips)
        {
            var path = $"platforms.{platform}";
            if (check.Mapping(platforms.Fields.GetValueOrDefault(platform), path) is not { } section)
            {
                continue;
            }
            check.Level(section, path, ["mst", "limits", "chips"]);
            if (check.Mapping(section.Fields.GetValueOrDefault("chips"), path + ".chips") is not { } chipMap)
            {
                continue;
            }
            check.Keys(chipMap, path + ".chips.", platformChips, required: true);
            foreach (var chip in platformChips)
            {
                if (check.Mapping(chipMap.Fields.GetValueOrDefault(chip), $"{path}.chips.{chip}") is { } chipSection)
                {
                    check.Level(chipSection, $"{path}.chips.{chip}", ["mst", "limits"]);
                }
            }
        }
    }

    private sealed class Checker(string file, bool isDefault, long? maxTiers, List<DatabaseError> errors)
    {
        // Extra keys are reported on their own line; missing ones (when required) on the line of the mapping.
        public void Keys(ProfileValue mapping, string prefix, IReadOnlyCollection<string> keys, bool required)
        {
            foreach (var (key, value) in mapping.Fields.Where(field => !keys.Contains(field.Key)))
            {
                errors.Add(new DatabaseError(file, value.Line, $"Unknown key '{prefix}{key}': every guide has the keys of guides/usb-helper.yaml."));
            }
            foreach (var key in keys.Where(key => required && !mapping.Fields.ContainsKey(key)))
            {
                errors.Add(new DatabaseError(file, mapping.Line, $"Missing key '{prefix}{key}': every guide has the keys of guides/usb-helper.yaml."));
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

        // A platform or chip: mst, limits (any of the block's keys) and, for a platform, its chips.
        public void Level(ProfileValue section, string path, string[] keys)
        {
            Keys(section, path + ".", keys, required: true);
            Mst(section.Fields.GetValueOrDefault("mst"), path + ".mst");
            if (Mapping(section.Fields.GetValueOrDefault("limits"), path + ".limits") is { } limits)
            {
                Block(limits, path + ".limits", all: false);
            }
        }

        /// <param name="all">Every key must be there and filled in: USB-Helper's global limits.</param>
        public void Block(ProfileValue block, string path, bool all)
        {
            Keys(block, path + ".", BlockKeys, all);
            foreach (var kind in Kinds)
            {
                Pair(block.Fields.GetValueOrDefault(kind), $"{path}.{kind}", kind == "tiers", all);
            }
            if (Mapping(block.Fields.GetValueOrDefault("buses"), path + ".buses") is { } buses)
            {
                Keys(buses, path + ".buses.", Buses, all);
                foreach (var bus in Buses)
                {
                    if (Mapping(buses.Fields.GetValueOrDefault(bus), $"{path}.buses.{bus}") is { } busBlock)
                    {
                        Keys(busBlock, $"{path}.buses.{bus}.", ["hubs"], required: true);
                        Pair(busBlock.Fields.GetValueOrDefault("hubs"), $"{path}.buses.{bus}.hubs", tiers: false, all);
                    }
                }
            }
            if (Mapping(block.Fields.GetValueOrDefault("camera_audio"), path + ".camera_audio") is { } camera)
            {
                Keys(camera, path + ".camera_audio.", ["tiers"], all);
                Pair(camera.Fields.GetValueOrDefault("tiers"), path + ".camera_audio.tiers", tiers: true, all);
            }
            if (block.Fields.GetValueOrDefault("exceeded_note") is { } note && !Empty(note, path + ".exceeded_note", required: false))
            {
                if (note.Scalar is not string text)
                {
                    errors.Add(new DatabaseError(file, note.Line, $"Key '{path}.exceeded_note': expected text."));
                }
                else if (text.Length > Guide.ExceededNoteMaxLength)
                {
                    errors.Add(new DatabaseError(file, note.Line,
                        $"Key '{path}.exceeded_note' has {text.Length} characters; at most {Guide.ExceededNoteMaxLength}."));
                }
            }
        }

        private void Mst(ProfileValue? value, string path)
        {
            if (value == null || Empty(value, path, isDefault))
            {
                return;
            }
            if (value.Scalar is not (0L or 1L))
            {
                errors.Add(new DatabaseError(file, value.Line, $"Key '{path}': expected 1 (yes) or 0 (no), or nothing for the value above."));
            }
        }

        private void Pair(ProfileValue? value, string path, bool tiers, bool all)
        {
            if (Mapping(value, path) is not { } pair)
            {
                return;
            }
            Keys(pair, path + ".", Values, required: true);
            foreach (var name in Values)
            {
                Number(pair.Fields.GetValueOrDefault(name), $"{path}.{name}", tiers, all);
            }
        }

        private void Number(ProfileValue? value, string path, bool tiers, bool all)
        {
            if (value == null || Empty(value, path, all) || value.Scalar is Guide.None)
            {
                return;
            }
            if (value.Scalar is not long number || number < 0)
            {
                errors.Add(new DatabaseError(file, value.Line, $"Key '{path}': expected a whole number, none, or nothing for the value above."));
            }
            else if (tiers && number > maxTiers)
            {
                errors.Add(new DatabaseError(file, value.Line, $"Key '{path}' ({number}) must not be above usb.max_tiers ({maxTiers}) in base.yaml."));
            }
        }

        // An empty value falls back to the value above it, and in the end to USB-Helper's, so USB-Helper must fill those in.
        private bool Empty(ProfileValue value, string path, bool required)
        {
            if (value.Kind != ValueKind.Null)
            {
                return false;
            }
            if (required)
            {
                errors.Add(new DatabaseError(file, value.Line, $"Key '{path}' must be filled in: guides/usb-helper.yaml is the fallback for every other guide (none states no limit)."));
            }
            return true;
        }
    }
}
