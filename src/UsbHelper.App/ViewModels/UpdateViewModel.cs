using System.Globalization;
using UsbHelper.App.Resources;
using UsbHelper.App.Update;
using UsbHelper.Core.Diagnostics;

namespace UsbHelper.App.ViewModels;

/// <summary>
/// The update notice in the main window (#123). <see cref="CheckAsync"/> runs after the window shows and never blocks
/// it; when a newer stable release or beta exists, the notice offers each with "Skip". A picked release is downloaded,
/// verified and unzipped next to the app (<see cref="PortableUpdate"/>), then started in place of this one.
/// </summary>
/// <param name="current">This app's version.</param>
/// <param name="appFolder">The folder this app runs from.</param>
/// <param name="restart">Starts the given exe and closes this app.</param>
public sealed class UpdateViewModel(HttpClient http, string current, string appFolder, Action<string> restart, EventLog log, TimeSpan timeout)
    : ObservableObject
{
    // A whole update download (about 80 MB): ends one that stalls, leaves time for a slow line.
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(10);

    private UpdateCheckResult? found;
    private bool isOffered;
    private bool isBusy;
    private string? status;

    public bool IsOffered
    {
        get => isOffered;
        private set => SetField(ref isOffered, value);
    }

    /// <summary>True while a download runs: the choices are disabled.</summary>
    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetField(ref isBusy, value))
            {
                OnPropertyChanged(nameof(CanChoose));
            }
        }
    }

    public bool CanChoose => !IsBusy;

    public string Message => string.Format(CultureInfo.CurrentCulture, Strings.UpdateAvailable, current);

    /// <summary>The stable choice's text, or null when no newer stable release exists.</summary>
    public string? StableText => found?.Stable is { } stable ? string.Format(CultureInfo.CurrentCulture, Strings.UpdateToStable, stable.Version) : null;

    /// <summary>The beta choice's text, or null when no newer beta exists.</summary>
    public string? BetaText => found?.Beta is { } beta ? string.Format(CultureInfo.CurrentCulture, Strings.UpdateToBeta, beta.Version) : null;

    /// <summary>The download's progress, or why the update failed.</summary>
    public string? Status
    {
        get => status;
        private set => SetField(ref status, value);
    }

    public RelayCommand UpdateToStable => new(() => _ = InstallAsync(stable: true));

    public RelayCommand UpdateToBeta => new(() => _ = InstallAsync(stable: false));

    public RelayCommand Skip => new(() =>
    {
        IsOffered = false;
        log.Add("Update skipped");
    });

    /// <summary>Asks GitHub for newer releases; shows the notice if there are any. Never throws.</summary>
    public async Task CheckAsync()
    {
        UpdateCheckResult result;
        try
        {
            result = await UpdateCheck.FindAsync(http, current, timeout);
        }
        catch (Exception e)
        {
            // The check handles every expected failure; anything else must not reach the window either.
            result = new UpdateCheckResult(null, null, $"Update check skipped: {e.Message}");
        }
        log.Add(result.Log);
        found = result;
        OnPropertyChanged(nameof(StableText));
        OnPropertyChanged(nameof(BetaText));
        IsOffered = result.Stable != null || result.Beta != null;
    }

    /// <summary>Downloads and starts the picked release. A failure is shown in <see cref="Status"/>; this app keeps running.</summary>
    public async Task InstallAsync(bool stable)
    {
        if (IsBusy || (stable ? found?.Stable : found?.Beta) is not { } release)
        {
            return;
        }
        IsBusy = true;
        try
        {
            using var cancel = new CancellationTokenSource(DownloadTimeout);
            var progress = new Progress<int>(percent =>
            {
                if (IsBusy)
                {
                    Status = string.Format(CultureInfo.CurrentCulture, Strings.UpdateDownloading, release.Version, percent);
                }
            });
            var exe = await PortableUpdate.DownloadAsync(http, release, appFolder, ((IProgress<int>)progress).Report, cancel.Token);
            log.Add($"Updated to {release.Version} in {Path.GetDirectoryName(exe)}; starting it");
            restart(exe);
        }
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException or OperationCanceledException
            or InvalidDataException or System.ComponentModel.Win32Exception)
        {
            log.Add($"Update to {release.Version} failed: {e.Message}");
            IsBusy = false;
            Status = string.Format(CultureInfo.CurrentCulture, Strings.UpdateFailed, e.Message);
        }
    }
}
