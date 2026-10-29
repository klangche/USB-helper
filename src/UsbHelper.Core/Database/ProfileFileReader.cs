using System.Globalization;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace UsbHelper.Core.Database;

/// <summary>Reads one YAML file into a <see cref="ProfileValue"/> tree, keeping file and line of every value.</summary>
internal static partial class ProfileFileReader
{
    /// <summary>Returns the top-level mapping, or null after adding the problem to <paramref name="errors"/>.</summary>
    public static ProfileValue? Read(string path, string file, List<DatabaseError> errors)
    {
        var stream = new YamlStream();
        try
        {
            using var reader = System.IO.File.OpenText(path);
            stream.Load(reader);
        }
        catch (YamlException e)
        {
            errors.Add(new DatabaseError(file, (int)e.Start.Line, (e.InnerException ?? e).Message));
            return null;
        }

        if (stream.Documents.Count != 1)
        {
            errors.Add(new DatabaseError(file, 0, "Expected exactly one YAML document."));
            return null;
        }

        var rootNode = stream.Documents[0].RootNode;
        var root = Convert(rootNode, file, (int)rootNode.Start.Line, errors);
        if (root.Kind != ValueKind.Mapping)
        {
            errors.Add(new DatabaseError(file, root.Line, "The top level must be a mapping."));
            return null;
        }
        return root;
    }

    // A field's line is the line of its key, so messages point at the field name.
    private static ProfileValue Convert(YamlNode node, string file, int line, List<DatabaseError> errors)
    {
        switch (node)
        {
            case YamlMappingNode mapping:
                var fields = new Dictionary<string, ProfileValue>();
                foreach (var (keyNode, valueNode) in mapping.Children)
                {
                    // YamlDotNet rejects duplicate keys while loading.
                    var key = keyNode is YamlScalarNode keyScalar ? keyScalar.Value ?? "" : keyNode.ToString();
                    fields.Add(key, Convert(valueNode, file, (int)keyNode.Start.Line, errors));
                }
                return ProfileValue.CreateMapping(fields, file, line);

            case YamlSequenceNode sequence:
                return ProfileValue.CreateList(sequence.Children.Select(child => Convert(child, file, (int)child.Start.Line, errors)).ToList(), file, line);

            case YamlScalarNode scalar:
                return ConvertScalar(scalar, file, line);

            default:
                errors.Add(new DatabaseError(file, line, "Unsupported YAML node."));
                return ProfileValue.CreateScalar(ValueKind.Null, null, file, line);
        }
    }

    // YAML 1.2 core schema for plain scalars. Quoted scalars are always strings.
    private static ProfileValue ConvertScalar(YamlScalarNode scalar, string file, int line)
    {
        var text = scalar.Value ?? "";
        if (scalar.Style != ScalarStyle.Plain)
        {
            return ProfileValue.CreateScalar(ValueKind.String, text, file, line);
        }
        if (text is "" or "~" or "null" or "Null" or "NULL")
        {
            return ProfileValue.CreateScalar(ValueKind.Null, null, file, line);
        }
        if (text is "true" or "True" or "TRUE" or "false" or "False" or "FALSE")
        {
            return ProfileValue.CreateScalar(ValueKind.Boolean, text.Equals("true", StringComparison.OrdinalIgnoreCase), file, line);
        }
        if (IntegerPattern().IsMatch(text) && long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
        {
            return ProfileValue.CreateScalar(ValueKind.Integer, number, file, line);
        }
        return ProfileValue.CreateScalar(ValueKind.String, text, file, line);
    }

    [GeneratedRegex("^[-+]?[0-9]+$")]
    private static partial Regex IntegerPattern();
}
