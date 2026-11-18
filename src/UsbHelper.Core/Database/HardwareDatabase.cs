namespace UsbHelper.Core.Database;

/// <summary>
/// The hardware database: <c>base.yaml</c> (facts from the USB specification), <c>platforms/*.yaml</c> (one operating
/// system each, with its chips), <c>guides/*.yaml</c> (limits and recommendations, one file per guide) and <c>computers/**/*.yaml</c>
/// (problem machines, each on one platform and chip). Files whose name starts with a dot, such as
/// <c>.template.yaml</c>, are not loaded. Files are only read.
/// The files come from a folder (the repository's <c>database/</c>) or from resources built into the app (#102).
/// </summary>
public sealed class HardwareDatabase
{
    private const string BaseFile = "base.yaml";

    /// <summary>The database format this code reads: <c>schema_version</c> in base.yaml, and the <c>v3/</c> folder on the database branch (#288).</summary>
    public const int SchemaVersion = 3;

    // Kept for guides read later, such as custom.yaml (#300): they are checked against the same platforms, chips and tiers.
    private readonly IReadOnlyDictionary<string, List<string>> chips;
    private readonly long? maxTiers;

    private HardwareDatabase(
        Dictionary<string, ResolvedProfile> platforms, Dictionary<string, ResolvedProfile> computers, Dictionary<string, Guide> guides,
        IReadOnlyDictionary<string, List<string>> chips, long? maxTiers)
    {
        Platforms = platforms;
        Computers = computers;
        Guides = guides;
        this.chips = chips;
        this.maxTiers = maxTiers;
    }

    /// <summary>One profile per operating system and chip, by id (platform and chip, for example <c>windows-intel</c>).</summary>
    public IReadOnlyDictionary<string, ResolvedProfile> Platforms { get; }

    public IReadOnlyDictionary<string, ResolvedProfile> Computers { get; }

    /// <summary>Every guide, selectable or not, by id (the file name).</summary>
    public IReadOnlyDictionary<string, Guide> Guides { get; }

    /// <summary>USB-Helper's guide (<see cref="Guide.DefaultId"/>): ticked at start, and the fallback of every other guide.</summary>
    public Guide DefaultGuide => Guides[Guide.DefaultId];

    /// <summary>Loads and validates the database in <paramref name="folder"/>.</summary>
    /// <exception cref="DatabaseException">The database has one or more problems; all are listed.</exception>
    public static HardwareDatabase Load(string folder) =>
        Load(Directory.Exists(folder)
            // .yml too, so a file with that ending is reported, not silently left out.
            ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Where(path => Path.GetExtension(path) is ".yaml" or ".yml")
                .ToDictionary(path => Path.GetRelativePath(folder, path).Replace('\\', '/'), File.ReadAllText)
            : []);

    /// <summary>Loads and validates the database from its files' contents.</summary>
    /// <param name="files">Each YAML file's content, by its path relative to the database folder, with '/' between folders.</param>
    /// <exception cref="DatabaseException">The database has one or more problems; all are listed.</exception>
    public static HardwareDatabase Load(IReadOnlyDictionary<string, string> files)
    {
        var errors = new List<DatabaseError>();
        var usb = ReadBase(files, errors);
        var chips = new Dictionary<string, List<string>>();
        var platforms = ReadPlatforms(files, usb, chips, errors);
        var computers = ReadComputers(files, platforms, errors);
        var guides = ReadGuides(files, usb, chips, errors);
        foreach (var file in files.Keys.Where(file => !IsRead(file)).Order(StringComparer.Ordinal))
        {
            errors.Add(new DatabaseError(file, 0,
                "This file is not read: only base.yaml, platforms/*.yaml, guides/*.yaml and the .yaml files below computers/ are."));
        }

        if (errors.Count > 0)
        {
            throw new DatabaseException(errors);
        }
        return new HardwareDatabase(platforms, computers, guides, chips, MaxTiers(usb));
    }

    /// <summary>
    /// Reads one more guide that is not part of the database, such as the user's <c>custom.yaml</c> (#300): validated as
    /// every guide in <c>guides/</c>, with USB-Helper's guide as its fallback.
    /// </summary>
    /// <param name="file">The name used in errors and as the guide's file, for example <c>custom.yaml</c>.</param>
    /// <exception cref="DatabaseException">The guide has one or more problems; all are listed.</exception>
    internal Guide ReadGuide(string yaml, string file, string id, Action<ProfileValue, List<DatabaseError>>? check = null)
    {
        var errors = new List<DatabaseError>();
        var guide = ReadGuide(yaml, file, id, chips, maxTiers, DefaultGuide, errors, check);
        if (errors.Count > 0 || guide == null)
        {
            throw new DatabaseException(errors);
        }
        return guide;
    }

    // chips: every platform file's chip keys, also of a file with errors, for the guides' shape.
    private static Dictionary<string, ResolvedProfile> ReadPlatforms(
        IReadOnlyDictionary<string, string> files, ProfileValue usb, Dictionary<string, List<string>> chips, List<DatabaseError> errors)
    {
        var platforms = new Dictionary<string, ResolvedProfile>();
        foreach (var file in FilesIn(files, "platforms", subfolders: false))
        {
            var root = ProfileFileReader.Read(files[file], file, errors);
            if (root == null)
            {
                continue;
            }
            ProfileSchema.Validate(root, ProfileSchema.PlatformFields, errors);
            var name = RequiredText(root, "name", errors);
            var chipList = root.Fields.GetValueOrDefault("chips");
            if (chipList is not { Kind: ValueKind.Mapping, Fields.Count: > 0 })
            {
                errors.Add(new DatabaseError(file, chipList?.Line ?? root.Line, "Field 'chips' is required: at least one chip, each with 'name' and 'built_in_hub_levels'."));
                continue;
            }
            var platform = FileId(file);
            chips[platform] = [.. chipList.Fields.Keys];
            var body = Without(root, "name", "chips");
            var statuses = new Dictionary<string, string?> { [BaseFile] = Status(usb), [file] = Status(body) };
            var platformRoot = ProfileMerger.Merge(usb, body);
            foreach (var (key, chip) in chipList.Fields.Where(chip => chip.Value.Kind == ValueKind.Mapping))
            {
                var chipName = RequiredText(chip, "name", errors, $"chips.{key}.");
                if (chip.Find("built_in_hub_levels") == null)
                {
                    errors.Add(new DatabaseError(file, chip.Line, $"Chip '{key}' must set 'built_in_hub_levels' (null when unknown)."));
                }
                if (name == null || chipName == null)
                {
                    continue;
                }
                var id = $"{platform}-{key}";
                var profile = new ResolvedProfile(id, [BaseFile, file], ProfileMerger.Merge(platformRoot, Without(chip, "name")), statuses)
                {
                    Platform = platform,
                    PlatformName = name,
                    Chip = key,
                    ChipName = chipName,
                };
                if (!platforms.TryAdd(id, profile))
                {
                    errors.Add(new DatabaseError(file, chip.Line, $"Duplicate id '{id}', already used in {platforms[id].Files[^1]}."));
                }
            }
        }
        return platforms;
    }

    private static Dictionary<string, ResolvedProfile> ReadComputers(
        IReadOnlyDictionary<string, string> files, Dictionary<string, ResolvedProfile> platforms, List<DatabaseError> errors)
    {
        var computers = new Dictionary<string, ResolvedProfile>();
        foreach (var file in FilesIn(files, "computers", subfolders: true))
        {
            var root = ProfileFileReader.Read(files[file], file, errors);
            if (root == null)
            {
                continue;
            }
            var (id, idLine) = ComputedId(root, errors);
            ProfileSchema.Validate(Without(root, "id"), ProfileSchema.ComputerFields, errors);
            foreach (var field in new[] { "manufacturer", "model" })
            {
                if (root.Find(field) == null)
                {
                    errors.Add(new DatabaseError(file, root.Line, $"Computer profiles must set '{field}' explicitly."));
                }
            }
            var platform = RequiredText(root, "platform", errors);
            var chip = RequiredText(root, "chip_family", errors);
            if (id == null || platform == null || chip == null)
            {
                continue;
            }
            if (computers.TryGetValue(id, out var other))
            {
                errors.Add(new DatabaseError(file, idLine, $"Duplicate id '{id}' (computed from manufacturer and model), already used in {other.Files[^1]}."));
                continue;
            }
            var parent = platforms.Values.FirstOrDefault(profile => profile.Platform == platform && profile.Chip == chip);
            if (parent == null)
            {
                var known = platforms.Values.Any(profile => profile.Platform == platform);
                errors.Add(known
                    ? new DatabaseError(file, root.Fields["chip_family"].Line, $"Platform '{platform}' has no chip '{chip}'.")
                    : new DatabaseError(file, root.Fields["platform"].Line, $"Platform '{platform}' does not exist (platforms/{platform}.yaml)."));
                continue;
            }
            var body = Without(root, "platform", "chip_family");
            var statuses = parent.Files.ToDictionary(path => path, parent.StatusOf);
            statuses[file] = Status(body);
            computers.Add(id, new ResolvedProfile(id, [.. parent.Files, file], ProfileMerger.Merge(parent.Root, body), statuses)
            {
                Platform = parent.Platform,
                PlatformName = parent.PlatformName,
                Chip = parent.Chip,
                ChipName = parent.ChipName,
            });
        }
        return computers;
    }

    // Each guide is one file (guides/<id>.yaml). USB-Helper's is read first: it is the fallback of every other.
    private static Dictionary<string, Guide> ReadGuides(
        IReadOnlyDictionary<string, string> files, ProfileValue usb, IReadOnlyDictionary<string, List<string>> chips, List<DatabaseError> errors)
    {
        var guides = new Dictionary<string, Guide>();
        var guideFiles = files.Keys.Where(IsGuideFile).ToDictionary(FileId);
        if (!guideFiles.ContainsKey(Guide.DefaultId))
        {
            errors.Add(new DatabaseError($"guides/{Guide.DefaultId}.yaml", 0, "File not found: USB-Helper's guide is the default and the fallback of every other guide."));
        }
        var maxTiers = MaxTiers(usb);
        Guide? fallback = null;
        foreach (var (id, file) in guideFiles.OrderBy(guide => guide.Key == Guide.DefaultId ? 0 : 1).ThenBy(guide => guide.Key, StringComparer.Ordinal))
        {
            var isDefault = id == Guide.DefaultId;
            if (ReadGuide(files[file], file, id, chips, maxTiers, isDefault ? null : fallback, errors) is not { } guide)
            {
                continue;
            }
            fallback = isDefault ? guide : fallback;
            guides.Add(id, guide);
        }
        return guides;
    }

    // One guide file; null when it cannot be used. A guide without fallback is USB-Helper's, whose values must all be filled in.
    private static Guide? ReadGuide(
        string yaml, string file, string id, IReadOnlyDictionary<string, List<string>> chips, long? maxTiers, Guide? fallback,
        List<DatabaseError> errors, Action<ProfileValue, List<DatabaseError>>? check = null)
    {
        var isDefault = id == Guide.DefaultId;
        var root = ProfileFileReader.Read(yaml, file, errors);
        if (root == null)
        {
            return null;
        }
        GuideSchema.Validate(root, chips, isDefault, maxTiers, errors);
        check?.Invoke(root, errors);
        var name = RequiredText(root, "name", errors);
        var selectable = root.Fields.GetValueOrDefault("selectable");
        if (selectable?.Kind != ValueKind.Boolean)
        {
            errors.Add(new DatabaseError(file, selectable?.Line ?? root.Line, "Field 'selectable' is required and must be true or false."));
        }
        // 1: a ProAV guide (shown with a ProAV mark), 0: a general-purpose one such as USB Standard (#299).
        var proav = root.Fields.GetValueOrDefault("proav");
        if (proav?.Scalar is not (0L or 1L))
        {
            errors.Add(new DatabaseError(file, proav?.Line ?? root.Line, "Field 'proav' is required and must be 1 (ProAV) or 0 (general purpose)."));
        }
        if (name == null || selectable?.Scalar is not bool isSelectable)
        {
            return null;
        }
        return new Guide(id, name, isSelectable, root, fallback);
    }

    private static long? MaxTiers(ProfileValue usb) => usb.Find("usb.max_tiers")?.Scalar as long?;

    // guides/<id>.yaml, directly in guides/.
    private static bool IsGuideFile(string file) => IsInFolder(file, "guides", subfolders: false);

    // A file whose name starts with a dot is left out on purpose; any other file must be one of the files that are read.
    private static bool IsRead(string file) =>
        file == BaseFile || file[(file.LastIndexOf('/') + 1)..].StartsWith('.')
        || IsInFolder(file, "platforms", subfolders: false) || IsGuideFile(file)
        || IsInFolder(file, "computers", subfolders: true);

    private static IEnumerable<string> FilesIn(IReadOnlyDictionary<string, string> files, string subfolder, bool subfolders) =>
        files.Keys.Where(file => IsInFolder(file, subfolder, subfolders)).Order(StringComparer.Ordinal);

    private static bool IsInFolder(string file, string subfolder, bool subfolders) =>
        file.StartsWith($"{subfolder}/", StringComparison.Ordinal) && file.EndsWith(".yaml", StringComparison.Ordinal)
        && (subfolders || file.LastIndexOf('/') == subfolder.Length)
        && !file[(file.LastIndexOf('/') + 1)..].StartsWith('.');

    // "platforms/macos.yaml" is the platform "macos".
    private static string FileId(string file) => file[(file.LastIndexOf('/') + 1)..^".yaml".Length];

    private static ProfileValue Without(ProfileValue mapping, params string[] fields) =>
        ProfileValue.CreateMapping(mapping.Fields.Where(field => !fields.Contains(field.Key)).ToDictionary(), mapping.File, mapping.Line);

    // The status a file writes itself; null (unverified) when it writes none, whatever it inherits.
    private static string? Status(ProfileValue profile) => profile.Find("verification.status")?.Scalar as string;

    // base.yaml without schema_version: the USB facts every profile starts from.
    private static ProfileValue ReadBase(IReadOnlyDictionary<string, string> files, List<DatabaseError> errors)
    {
        var empty = ProfileValue.CreateMapping(new Dictionary<string, ProfileValue>(), BaseFile, 0);
        if (!files.TryGetValue(BaseFile, out var yaml))
        {
            errors.Add(new DatabaseError(BaseFile, 0, "File not found."));
            return empty;
        }
        var root = ProfileFileReader.Read(yaml, BaseFile, errors);
        if (root == null)
        {
            return empty;
        }

        var body = Without(root, "schema_version");
        ProfileSchema.Validate(body, ProfileSchema.BaseFields, errors);
        if (!root.Fields.TryGetValue("schema_version", out var version) || version.Scalar is not long number || number != SchemaVersion)
        {
            errors.Add(new DatabaseError(BaseFile, version?.Line ?? root.Line, $"'schema_version' must be {SchemaVersion}."));
        }
        if (body.Find("usb.max_tiers") == null)
        {
            errors.Add(new DatabaseError(BaseFile, root.Line, "Field 'usb.max_tiers' is required (null when unknown)."));
        }
        return body;
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

    private static string? RequiredText(ProfileValue mapping, string field, List<DatabaseError> errors, string prefix = "")
    {
        if (mapping.Fields.TryGetValue(field, out var value) && value.Scalar is string text && text.Length > 0)
        {
            return text;
        }
        errors.Add(new DatabaseError(mapping.File, value?.Line ?? mapping.Line, $"Field '{prefix}{field}' is required and must be text."));
        return null;
    }
}
