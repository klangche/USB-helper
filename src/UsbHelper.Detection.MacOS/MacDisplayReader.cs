using System.Diagnostics;
using UsbHelper.Core.Detection;

namespace UsbHelper.Detection.MacOS;

/// <summary>Runs <c>system_profiler -json</c> for one data type, so the reader can be tested without a Mac.</summary>
public interface IProfilerSource
{
    /// <exception cref="InvalidOperationException">system_profiler could not run or failed.</exception>
    string Read(string dataType);
}

/// <summary>The real <c>/usr/sbin/system_profiler</c>. No administrator rights.</summary>
public sealed class SystemProfiler : IProfilerSource
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public string Read(string dataType)
    {
        var info = new ProcessStartInfo("/usr/sbin/system_profiler") { RedirectStandardOutput = true, UseShellExecute = false };
        info.ArgumentList.Add("-json");
        info.ArgumentList.Add(dataType);
        try
        {
            using var process = Process.Start(info) ?? throw new InvalidOperationException("system_profiler did not start.");
            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(Timeout))
            {
                process.Kill();
                throw new InvalidOperationException("system_profiler timed out.");
            }
            return process.ExitCode == 0 ? output.Result : throw new InvalidOperationException($"system_profiler exited with {process.ExitCode}.");
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new InvalidOperationException("system_profiler could not run.", exception);
        }
    }
}

/// <summary>
/// Reads the active monitors and the Thunderbolt/USB4 context of a Mac from System Information (#158). Reads no
/// bandwidth or refresh rate, does not count a Thunderbolt display as a USB hub (that only happens when it appears in
/// the USB topology) and does not tie a monitor to a USB port.
/// </summary>
public sealed class MacDisplayReader(IProfilerSource source)
{
    public MacDisplayReader() : this(new SystemProfiler())
    {
    }

    /// <summary>Each active monitor once; the built-in panel is <see cref="MonitorOutput.Internal"/>, every other output <see cref="MonitorOutput.Other"/>.</summary>
    /// <exception cref="InvalidOperationException">system_profiler failed or gave output that is not JSON.</exception>
    public IReadOnlyList<DisplayMonitor> ReadMonitors() => Run(MacDisplays.Parse, "SPDisplaysDataType");

    /// <summary>The Thunderbolt/USB4 buses with their receptacle and attached devices.</summary>
    /// <exception cref="InvalidOperationException">system_profiler failed or gave output that is not JSON.</exception>
    public IReadOnlyList<ThunderboltBus> ReadThunderbolt() => Run(MacThunderbolt.Parse, "SPThunderboltDataType");

    /// <summary>Counts active external displays (see <see cref="MonitorOutputs.IsExternal"/>).</summary>
    public int CountExternalDisplays() => DisplayMonitor.ExternalCount(ReadMonitors())!.Value;

    private T Run<T>(Func<string, T> parse, string dataType)
    {
        try
        {
            return parse(source.Read(dataType));
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new InvalidOperationException($"system_profiler gave no valid JSON for {dataType}.", exception);
        }
    }
}
