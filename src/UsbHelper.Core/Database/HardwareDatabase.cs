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
                var computed = profile.IdComputed ? " (computed from manufacturer and model)" : "";
                errors.Add(new DatabaseError(profile.Body.File, profile.IdLine, $"Duplicate id '{profile.Id}'{computed}, already used in {seen[profile.Id]}."));
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
            var root = ProfileMerger.Merge(defaults, profile.Body);
            var statuses = new Dictionary<string, string?> { [BaseFile] = Status(defaults), [profile.Body.File] = Status(profile.Body) };
            platforms.TryAdd(profile.Id, new ResolvedProfile(profile.Id, [BaseFile, profile.Body.File], root, statuses));
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
            var root = ProfileMerger.Merge(parent.Root, profile.Body);
            var statuses = parent.Files.ToDictionary(file => file, parent.StatusOf);
            statuses[profile.Body.File] = Status(profile.Body);
            computers.TryAdd(profile.Id, new ResolvedProfile(profile.Id, [.. parent.Files, profile.Body.File], root, statuses));
        }

        if (errors.Count > 0)
        {
            throw new DatabaseException(errors);
        }
        return new HardwareDatabase(platforms, computers);
    }

    // The status a file writes itself; null (unverified) when it writes none, whatever it inherits.
    private static string? Status(ProfileValue profile) => profile.Find("verification.status")?.Scalar as string;

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

            var (id, idLine) = isComputer ? ComputedId(root, errors) : (RequiredText(root, "id", errors), root.Fields.GetValueOrDefault("id")?.Line ?? 0);
            var inherits = RequiredText(root, "inherits", errors);
            ProfileSchema.Validate(root, IdentityFields, errors);
            if (isComputer)
            {
                foreach (var field in new[] { "manufacturer", "model" })
                {
                    if (root.Find(field) == null)
                    {
                        errors.Add(new DatabaseError(file, root.Line, $"Computer profiles must set '{field}' explicitly."));
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
            profiles.Add(new ProfileFile(id, idLine, isComputer, inherits, root.Fields["inherits"].Line, body));
        }
        return profiles;
    }

    // A computer profile's id is never written: it is computed from manufacturer and model, so nobody can set it.
    private static (string? Id, int Line) ComputedId(ProfileValue root, List<DatabaseError> errors)
    {
        if (root.Fields.TryGetValue("id", out var written))
        {
            errors.Add(new DatabaseError(root.File, written.Line, "Computer profiles have no 'id': it is computed from manufacturer and model."));
        }
        var manufacturer = root.Fields.GetValueOrDefault("manufacturer");
        var model = root.Fields.GetValueOrDefault("model");
        if (manufacturer?.Scalar is string manufacturerText && model?.Scalar is string modelText
            && !string.IsNullOrWhiteSpace(manufacturerText) && !string.IsNullOrWhiteSpace(modelText))
        {
            return (ComputerProfileId.From(manufacturerText, modelText), model.Line);
        }
        if (manufacturer != null && model != null)
        {
            var notText = manufacturer.Scalar is string text && !string.IsNullOrWhiteSpace(text) ? model : manufacturer;
            errors.Add(new DatabaseError(root.File, notText.Line, "'manufacturer' and 'model' must be text: the id is computed from them."));
        }
        return (null, 0);
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

    private sealed record ProfileFile(string Id, int IdLine, bool IdComputed, string Inherits, int InheritsLine, ProfileValue Body);
}
