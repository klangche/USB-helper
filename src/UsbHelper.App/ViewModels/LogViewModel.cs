using System.Collections.ObjectModel;
using System.Globalization;
using UsbHelper.Core.Diagnostics;

namespace UsbHelper.App.ViewModels;

/// <summary>The log window: every logged event of this session, newest at the bottom, and its export.</summary>
public sealed class LogViewModel
{
    private readonly EventLog log;

    /// <summary>Follows <paramref name="log"/>; entries must be added on the UI thread.</summary>
    public LogViewModel(EventLog log)
    {
        this.log = log;
        Lines = [.. log.Entries.Select(entry => entry.ToString())];
        log.Added += entry => Lines.Add(entry.ToString());
        log.Cleared += Lines.Clear;
    }

    public ObservableCollection<string> Lines { get; }

    public string ExportFileName => $"usb-helper-log-{DateTime.Now.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture)}.txt";

    /// <summary>The whole log as plain text, unknown findings included.</summary>
    public string Export() => log.Export();
}
