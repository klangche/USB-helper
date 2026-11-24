using System.Globalization;
using UsbHelper.App.Resources;
using UsbHelper.Core.Database;
using UsbHelper.Core.Topology;

namespace UsbHelper.App.ViewModels;

/// <summary>
/// The selected philosophy's notes on a marked cable (#372), for its hover and the report: what it says about cables of
/// that type, its approved models, and the marked model's own facts and restrictions. Notes only: never a status.
/// </summary>
public static class CableNotes
{
    public static IReadOnlyList<string> For(Guide? guide, CableMark? mark)
    {
        if (guide == null || mark?.Metres == null)
        {
            return [];
        }
        var kind = mark.Kind ?? "active_copper"; // A mark from before #372 is an active cable.
        var bus = mark.Bus == Core.Detection.UsbBus.Usb3 ? "usb3" : "usb2";
        var lines = new List<string>();
        var rated = string.Join("; ", guide.CableModels.Where(model => model.Type == kind)
            .Select(model => string.Format(CultureInfo.CurrentCulture, Strings.CableNoteRated, model.Name, model.Length)));
        foreach (var note in guide.CableNotes.Where(note => note.Type == kind && (note.Bus == null || note.Bus == bus)))
        {
            lines.Add($"{guide.Name}: {note.Text}" + (rated.Length > 0 ? $"; {rated}" : ""));
        }
        if (guide.CableModels.FirstOrDefault(model => model.Name == mark.Model) is { } marked)
        {
            var adds = marked.Hubs is 0 && marked.Tiers is 0 ? Strings.CableNoteAddsNothing
                : marked.Hubs != null || marked.Tiers != null
                    ? string.Format(CultureInfo.CurrentCulture, Strings.CableNoteAdds, marked.Hubs ?? 0, marked.Tiers ?? 0)
                    : null;
            var facts = new[] { string.Format(CultureInfo.CurrentCulture, Strings.CableNoteLength, marked.Length, Kind(marked.Type)), marked.Speed, marked.Note, adds }
                .OfType<string>();
            lines.Add($"{guide.Name} {marked.Name}: {string.Join(", ", facts)}");
            if (!marked.HostAllowed && mark.Host)
            {
                lines.Add(string.Format(CultureInfo.CurrentCulture, Strings.CableNoteNotHost, guide.Name, marked.Name));
            }
        }
        return lines;
    }

    /// <summary>A cable type in words: "passive", "active optical".</summary>
    public static string Kind(string kind) => kind switch
    {
        CableMark.PassiveKind => Strings.CableKindPassive,
        "active_copper" => Strings.CableKindActiveCopper,
        "active_optical" => Strings.CableKindActiveOptical,
        "usb4" => Strings.CableKindUsb4,
        _ => kind,
    };
}
