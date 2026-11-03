using System.Text;
using UsbHelper.App.Resources;

namespace UsbHelper.App.ViewModels;

/// <summary>
/// The tree as plain text (#134), as the window shows it: each row with its badges in brackets, its name, the result word
/// after " - " and its notes in parentheses. Collapsed items are written out too. ASCII tree lines, so the text survives
/// an email and is easy to edit by hand.
/// </summary>
public static class TreeText
{
    public static string Render(NodeViewModel root)
    {
        var lines = new List<string> { Row(root) };
        AddChildren(root, "", lines);
        return string.Join(Environment.NewLine, lines);
    }

    private static void AddChildren(NodeViewModel item, string indent, List<string> lines)
    {
        foreach (var child in item.Children)
        {
            lines.Add(indent + (child.IsLast ? "`-- " : "|-- ") + Row(child));
            AddChildren(child, indent + (child.IsLast ? "    " : "|   "), lines);
        }
    }

    private static string Row(NodeViewModel item)
    {
        var row = new StringBuilder();
        if (item.MarkBadge is { } mark)
        {
            row.Append('[').Append(mark).Append("] ");
        }
        if (item.IsDisplay)
        {
            row.Append('[').Append(Strings.DisplayBadge).Append("] ");
        }
        row.Append(item.Text);
        if (item.PortType is { } portType)
        {
            row.Append(" [").Append(portType).Append(']');
        }
        if (item.StatusWord is { } word)
        {
            row.Append(" - ").Append(word);
        }
        foreach (var note in new[] { item.DisplayNote, item.Counts }.OfType<string>())
        {
            row.Append(" (").Append(note).Append(')');
        }
        return row.ToString();
    }
}
