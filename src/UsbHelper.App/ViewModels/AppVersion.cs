using System.Reflection;

namespace UsbHelper.App.ViewModels;

internal static class AppVersion
{
    /// <summary>The running version; the release build sets it. "+commit" metadata is left out.</summary>
    public static string Current { get; } =
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "unknown";
}
