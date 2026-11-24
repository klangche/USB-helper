using System.Globalization;
using UsbHelper.App.Resources;
using UsbHelper.App.Sources;
using UsbHelper.Core.Database;

namespace UsbHelper.App;

/// <summary>One line of a philosophy's details panel (#397): a heading, a line, or a quote under a line.</summary>
public sealed record GuideInfoLine(string Text, bool IsHeading = false, bool IsQuote = false)
{
    public double Indent => IsHeading ? 0 : IsQuote ? 24 : 12;
}

/// <summary>
/// A philosophy's details behind its ⓘ button (#397): a short hover (its menu line, who signed it off, its source) and a
/// panel with everything: what it counts in words, every limit with its quote and source, its cable notes and models,
/// and its source files. The window shows only the result; this is where the philosophy is explained.
/// </summary>
public sealed class GuideInfo
{
    private GuideInfo(string summary, IReadOnlyList<GuideInfoLine> lines, IReadOnlyList<SourceEntry> sources)
    {
        Summary = summary;
        Lines = lines;
        Sources = sources;
    }

    /// <summary>The hover: the menu line, the sign-off with the file (#389) and the source titles, one per line.</summary>
    public string Summary { get; }

    public IReadOnlyList<GuideInfoLine> Lines { get; }

    /// <summary>The source PDFs and links (#333); a PDF opens in the built-in viewer.</summary>
    public IReadOnlyList<SourceEntry> Sources { get; }

    public bool HasSources => Sources.Count > 0;

    /// <summary>The details of a v4 philosophy; null for a v3 guide, which has no rules to explain.</summary>
    public static GuideInfo? Of(Guide? guide, IReadOnlyDictionary<string, ResolvedProfile>? platforms = null)
    {
        if (guide is not { Format: >= 4 })
        {
            return null;
        }
        var titles = guide.SourceDefinitions.Values.Select(source => source.Title).OfType<string>().Distinct().ToList();
        var summary = string.Join('\n', new[]
        {
            GuideLine.For(guide, platforms),
            guide.VersionHover,
            titles.Count == 0 ? "" : string.Format(CultureInfo.CurrentCulture, Strings.GuideInfoSource, string.Join("; ", titles)),
        }.Where(line => line.Length > 0));
        var lines = new List<GuideInfoLine>();
        if (guide.NeedsNewerApp)
        {
            lines.Add(new(Strings.GuideNeedsNewer));
            return new GuideInfo(summary, lines, []);
        }
        lines.Add(new(Strings.GuideInfoCountsHeading, IsHeading: true));
        lines.AddRange(guide.MeasureDefinitions.Values.Select(measure => new GuideInfoLine(GuideLine.Counts(measure))));
        lines.Add(new(Strings.GuideInfoLimitsHeading, IsHeading: true));
        foreach (var rule in guide.Rules)
        {
            lines.Add(new(GuideLine.Part(rule, platforms)));
            var source = rule.Source is { } id && guide.SourceDefinitions.GetValueOrDefault(id) is { } found ? found.Title ?? id : null;
            if (rule.Quote != null || source != null)
            {
                lines.Add(new(rule.Quote == null ? $"({source})"
                    : source == null ? $"\u201c{rule.Quote}\u201d" : $"\u201c{rule.Quote}\u201d ({source})", IsQuote: true));
            }
        }
        if (guide.CableNotes.Count > 0 || guide.CableModels.Count > 0)
        {
            lines.Add(new(Strings.GuideInfoCablesHeading, IsHeading: true));
            lines.AddRange(guide.CableNotes.Select(note => new GuideInfoLine(note.Text)));
            lines.AddRange(guide.CableModels.Select(model => new GuideInfoLine(string.Format(CultureInfo.CurrentCulture, Strings.GuideInfoCableModel,
                model.Name, model.Length.ToString("0.#", CultureInfo.CurrentCulture)))));
        }
        lines.Add(new(guide.VersionHover, IsHeading: true));
        return new GuideInfo(summary, lines, SourceRouter.EntriesOf(guide));
    }
}
