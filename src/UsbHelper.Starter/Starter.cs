using System.Diagnostics;
using System.Globalization;
using UsbHelper.Starter.Resources;

namespace UsbHelper.Starter;

/// <summary>
/// USB-helper.exe (#130): the only exe at the top of the portable zip and the install folder. The app, its DLLs and the
/// .NET runtime are in the folder "app" next to it; this starts app\UsbHelper.exe with the same arguments and exits.
/// </summary>
public static class Starter
{
    /// <summary>The folder next to the starter that holds the app.</summary>
    public const string AppFolder = "app";

    /// <summary>The app's exe in <see cref="AppFolder"/>.</summary>
    public const string AppExe = "UsbHelper.exe";

    public static string AppPath(string starterFolder) => Path.Combine(starterFolder, AppFolder, AppExe);

    /// <summary>Why the app cannot be started, or null when it can.</summary>
    public static string? Check(string starterFolder) =>
        File.Exists(AppPath(starterFolder))
            ? null
            : string.Format(CultureInfo.CurrentCulture, Strings.StarterNoApp, AppPath(starterFolder));

    public static ProcessStartInfo StartInfo(string app, IEnumerable<string> args, string workingDirectory)
    {
        var start = new ProcessStartInfo(app) { UseShellExecute = false, WorkingDirectory = workingDirectory };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }
        return start;
    }
}
