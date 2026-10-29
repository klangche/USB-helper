namespace UsbHelper.Core.Database;

/// <summary>
/// The hardware database: <c>base.yaml</c>, <c>platforms/*.yaml</c> and <c>computers/**/*.yaml</c>.
/// A platform inherits from <c>base</c>; a computer inherits from a platform.
/// Files whose name starts with a dot, such as <c>.template.yaml</c>, are not loaded. Files are only read.
/// </summary>
public sealed class HardwareDatabase
{
    private const string BaseFile = "base.yaml";
    private const int SchemaVersion = 1;
    private static readonly IReadOnlySet<string> IdentityFields = new HashSet<string> { "id", "inherits" };

    private HardwareDatabase(Dictionary<string, ResolvedProfile> platforms, Dictionary<string, ResolvedProfile> computers)
    {
        Platforms = platforms;
        Computers = computers;
    }

    public IReadOnlyDictionary<string, ResolvedProfile> Platforms { get; }

    public IReadOnlyDictionary<string, ResolvedProfile> Computers { get; }

    /// <summary>Loads and validates the database in <paramref name="folder"/>.</summary>
    /// <exception cref="DatabaseException">The database has one or more problems; all are listed.</exception>
    public static HardwareDatabase Load(string folder)
    {
        var errors = new List<DatabaseError>();
        var defaults = ReadBase(folder, errors);
        var platformFiles = ReadProfiles(folder, "platforms", SearchOption.TopDirectoryOnly, isComputer: false, errors);
        var computerFiles = ReadProfiles(folder, "computers", SearchOption.AllDirectories, isComputer: true, errors);

        var seen = new Dictionary<string, string>();
        foreach (var profile in platformFiles.Concat(computerFiles))
        {
            if (!seen.TryAdd(profile.Id, profile.Body.File))
            {
                errors.Add(new DatabaseError(profile.Body.File, profile.IdLine, $"Duplicate id '{profile.Id}', already used in {seen[profile.Id]}."));
            }
        }

        var platforms = new Dictionary<string, ResolvedProfile>();
        foreach (var profile in platformFiles)
        {
            if (profile.Inherits != "base")
            {
                errors.Add(new DatabaseError(profile.Body.File, profile.InheritsLine, $"A platform profile must inherit from 'base', not '{profile.Inherits}'."));
                continue;
            }
            platforms.TryAdd(profile.Id, new ResolvedProfile(profile.Id, [BaseFile, profile.Body.File], ProfileMerger.Merge(defaults, profile.Body)));
        }

        var computers = new Dictionary<string, ResolvedProfile>();
        foreach (var profile in computerFiles)
        {
            if (!platforms.TryGetValue(profile.Inherits, out var parent))
            {
                var reason = profile.Inherits == "base" || computerFiles.Any(other => other.Id == profile.Inherits)
                    ? $"'{profile.Inherits}' is not a platform profile; a computer profile must inherit from a platform profile."
                    : $"Parent profile '{profile.Inherits}' does not exist.";
                errors.Add(new DatabaseError(profile.Body.File, profile.InheritsLine, reason));
                continue;
            }
            computers.TryAdd(profile.Id, new ResolvedProfile(profile.Id, [.. parent.Files, profile.Body.File], ProfileMerger.Merge(parent.Root, profile.Body)));
        }

        if (errors.Count > 0)
        {
            throw new DatabaseException(errors);
        }
        return new HardwareDatabase(platforms, computers);
    }

    private static ProfileValue ReadBase(string folder, List<DatabaseError> errors)
    {
        var empty = ProfileValue.CreateMapping(new Dictionary<string, ProfileValue>(), BaseFile, 0);
        var path = Path.Combine(folder, BaseFile);
        if (!File.Exists(path))
        {
            errors.Add(new DatabaseError(BaseFile, 0, "File not found."));
            return empty;
        }
        var root = ProfileFileReader.Read(path, BaseFile, errors);
        if (root == null)
        {
            return empty;
        }

        foreach (var (key, value) in root.Fields)
        {
            if (key is not ("schema_version" or "defaults"))
            {
                errors.Add(new DatabaseError(BaseFile, value.Line, $"Unknown field '{key}'."));
            }
        }
        if (!root.Fields.TryGetValue("schema_version", out var version) || version.Scalar is not long number || number != SchemaVersion)
        {
            errors.Add(new DatabaseError(BaseFile, version?.Line ?? root.Line, $"'schema_version' must be {SchemaVersion}."));
        }
        if (!root.Fields.TryGetValue("defaults", out var defaults))
        {
            return empty;
        }
        if (defaults.Kind != ValueKind.Mapping)
        {
            errors.Add(new DatabaseError(BaseFile, defaults.Line, "Field 'defaults': expected a mapping."));
            return empty;
        }
        ProfileSchema.Validate(defaults, new HashSet<string>(), errors);
        ProfileSchema.RejectComputerOnlyFields(defaults, "base.yaml", errors);
        return defaults;
    }

    private static List<ProfileFile> ReadProfiles(string folder, string subfolder, SearchOption search, bool isComputer, List<DatabaseError> errors)
    {
        var profiles = new List<ProfileFile>();
        var directory = Path.Combine(folder, subfolder);
        if (!Directory.Exists(directory))
        {
            return profiles;
        }

        var paths = Directory.GetFiles(directory, "*.yaml", search)
            .Where(path => !Path.GetFileName(path).StartsWith('.'))
            .Order(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var file = Path.GetRelativePath(folder, path).Replace('\\', '/');
            var root = ProfileFileReader.Read(path, file, errors);
            if (root == null)
            {
                continue;
            }

            var id = RequiredText(root, "id", errors);
            var inherits = RequiredText(root, "inherits", errors);
            ProfileSchema.Validate(root, IdentityFields, errors);
            if (isComputer)
            {
                foreach (var field in new[] { "manufacturer", "model", "limits.max_hubs", "limits.max_tiers", "limits.max_hops" })
                {
                    if (root.Find(field) == null)
                    {
                        errors.Add(new DatabaseError(file, root.Line, $"Computer profiles must set '{field}' explicitly (null if unknown)."));
                    }
                }
            }
            else
            {
                ProfileSchema.RejectComputerOnlyFields(root, "platform profiles", errors);
            }
            if (id == null || inherits == null)
            {
                continue;
            }

            var body = ProfileValue.CreateMapping(
                root.Fields.Where(field => !IdentityFields.Contains(field.Key)).ToDictionary(),
                file, root.Line);
            profiles.Add(new ProfileFile(id, root.Fields["id"].Line, inherits, root.Fields["inherits"].Line, body));
        }
        return profiles;
    }

    private static string? RequiredText(ProfileValue root, string field, List<DatabaseError> errors)
    {
        if (root.Fields.TryGetValue(field, out var value) && value.Scalar is string text && text.Length > 0)
        {
            return text;
        }
        errors.Add(new DatabaseError(root.File, value?.Line ?? root.Line, $"Field '{field}' is required and must be text."));
        return null;
    }

    private sealed record ProfileFile(string Id, int IdLine, string Inherits, int InheritsLine, ProfileValue Body);
}
