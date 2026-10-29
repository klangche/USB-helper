namespace UsbHelper.Core.Database;

/// <summary>
/// Applies a child profile on top of its parent:
/// an omitted field inherits, a value overrides, an explicit null overrides and means unknown,
/// mappings merge field by field, lists append the child's items to the parent's.
/// An empty list or mapping therefore adds nothing and never erases inherited data.
/// </summary>
internal static class ProfileMerger
{
    public static ProfileValue Merge(ProfileValue parent, ProfileValue child)
    {
        if (parent.Kind == ValueKind.Mapping && child.Kind == ValueKind.Mapping)
        {
            var fields = new Dictionary<string, ProfileValue>(parent.Fields);
            foreach (var (key, value) in child.Fields)
            {
                fields[key] = fields.TryGetValue(key, out var inherited) ? Merge(inherited, value) : value;
            }
            return ProfileValue.CreateMapping(fields, child.File, child.Line);
        }
        if (parent.Kind == ValueKind.List && child.Kind == ValueKind.List)
        {
            return child.Items.Count == 0 ? parent : ProfileValue.CreateList([.. parent.Items, .. child.Items], child.File, child.Line);
        }
        return child;
    }
}
