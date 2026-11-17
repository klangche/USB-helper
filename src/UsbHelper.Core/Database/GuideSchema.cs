namespace UsbHelper.Core.Database;

/// <summary>
/// The shape every guide file must have (owner decision 2026-11-17, #285): the header, a <c>limits</c> block for all
/// platforms, and under <c>platforms</c> every platform and chip of <c>platforms/*.yaml</c>, each with <c>mst</c> and a
/// <c>limits</c> block. A returned file with a missing or extra key is rejected, naming file, line and key.
/// </summary>
internal static class GuideSchema
{
    private static readonly string[] Header = ["name", "selectable", "notes", "sources", "verification"];
    private static readonly string[] Kinds = ["hubs", "tiers", "hops"];
    private static readonly string[] Values = ["recommended", "max"];

    /// <param name="chips">Every platform's chip keys, from <c>platforms/*.yaml</c>.</param>
    /// <param name="isDefault">USB-Helper's guide: the fallback, so every number and <c>mst</c> must be filled in.</param>
    /// <param name="maxTiers"><c>usb.max_tiers</c> from <c>base.yaml</c>; no tier value may be above it.</param>
    public static void Validate(
        ProfileValue root, IReadOnlyDictionary<string, IReadOnlyList<string>> chips, bool isDefault, long? maxTiers, List<DatabaseError> errors)
    {
        var check = new Checker(root.File, isDefault, maxTiers, errors);
        ProfileSchema.Validate(
            ProfileValue.CreateMapping(root.Fields.Where(field => Header.Contains(field.Key)).ToDictionary(), root.File, root.Line),
            ProfileSchema.GuideHeaderFields, errors);
        check.Keys(root, "", [.. Header, "limits", "platforms"], required: ["limits", "platforms"]);
        check.Block(root.Fields.GetValueOrDefault("limits"), "limits");
        if (check.Mapping(root.Fields.GetValueOrDefault("platforms"), "platforms") is not { } platforms)
        {
            return;
        }
        check.Keys(platforms, "platforms.", chips.Keys, chips.Keys);
        foreach (var (platform, platformChips) in chips)
        {
            var path = $"platforms.{platform}";
            if (check.Mapping(platforms.Fields.GetValueOrDefault(platform), path) is not { } section)
            {
                continue;
            }
            check.Keys(section, path + ".", ["mst", "limits", "chips"], ["mst", "limits", "chips"]);
            check.Mst(section.Fields.GetValueOrDefault("mst"), path + ".mst");
            check.Block(section.Fields.GetValueOrDefault("limits"), path + ".limits");
            if (check.Mapping(section.Fields.GetValueOrDefault("chips"), path + ".chips") is not { } chipSections)
            {
                continue;
            }
            check.Keys(chipSections, path + ".chips.", platformChips, platformChips);
            foreach (var chip in platformChips)
            {
                var chipPath = $"{path}.chips.{chip}";
                if (check.Mapping(chipSections.Fields.GetValueOrDefault(chip), chipPath) is { } chipSection)
                {
                    check.Keys(chipSection, chipPath + ".", ["mst", "limits"], ["mst", "limits"]);
                    check.Mst(chipSection.Fields.GetValueOrDefault("mst"), chipPath + ".mst");
                    check.Block(chipSection.Fields.GetValueOrDefault("limits"), chipPath + ".limits");
                }
            }
        }
    }

    private sealed class Checker(string file, bool isDefault, long? maxTiers, List<DatabaseError> errors)
    {
        // Missing keys are reported on the line of the mapping that should hold them; extra keys on their own line.
        public void Keys(ProfileValue mapping, string prefix, IEnumerable<string> allowed, IEnumerable<string> required)
        {
            foreach (var (key, value) in mapping.Fields.Where(field => !allowed.Contains(field.Key)))
            {
                errors.Add(new DatabaseError(file, value.Line, $"Unknown key '{prefix}{key}': every guide file has the shape of guides/usb-helper.yaml."));
            }
            foreach (var key in required.Where(key => !mapping.Fields.ContainsKey(key)))
            {
                errors.Add(new DatabaseError(file, mapping.Line, $"Missing key '{prefix}{key}': every guide file has the shape of guides/usb-helper.yaml."));
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

        public void Block(ProfileValue? value, string path)
        {
            if (Mapping(value, path) is not { } block)
            {
                return;
            }
            Keys(block, path + ".", [.. Kinds, "camera_audio", "exceeded_note"], [.. Kinds, "camera_audio", "exceeded_note"]);
            foreach (var kind in Kinds)
            {
                Pair(block.Fields.GetValueOrDefault(kind), $"{path}.{kind}", kind == "tiers");
            }
            if (Mapping(block.Fields.GetValueOrDefault("camera_audio"), path + ".camera_audio") is { } camera)
            {
                Keys(camera, path + ".camera_audio.", ["tiers", "hops"], ["tiers", "hops"]);
                Pair(camera.Fields.GetValueOrDefault("tiers"), path + ".camera_audio.tiers", tiers: true);
                Pair(camera.Fields.GetValueOrDefault("hops"), path + ".camera_audio.hops", tiers: false);
            }
            if (block.Fields.GetValueOrDefault("exceeded_note") is { Kind: not ValueKind.Null } note)
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

        public void Mst(ProfileValue? value, string path)
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
            Keys(pair, path + ".", Values, Values);
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

        // An empty value falls back to USB-Helper's, so USB-Helper's own file must fill every one in.
        private bool Empty(ProfileValue value, string path)
        {
            if (value.Kind != ValueKind.Null)
            {
                return false;
            }
            if (isDefault)
            {
                errors.Add(new DatabaseError(file, value.Line, $"Key '{path}' must be filled in: guides/usb-helper.yaml is the fallback for every other guide (none states no limit)."));
            }
            return true;
        }
    }
}
