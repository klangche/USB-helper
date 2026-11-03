using System.Globalization;
using UsbHelper.App.Resources;
using UsbHelper.App.Update;
using UsbHelper.Core.Diagnostics;

namespace UsbHelper.App.ViewModels;

/// <summary>
/// The update popup (#123, #131): shown while <see cref="IsOffered"/>. <see cref="CheckAsync"/> runs after the window
/// shows and never blocks it; when a newer stable release or beta exists, the popup offers each with "Skip". Help > Check
/// for updates (<see cref="CheckNow"/>) runs it again and also says when there is nothing newer or the check failed. A
/// picked release: an .msi install downloads and verifies the .msi (#124) and hands it to <paramref name="restart"/>; the
/// portable app opens the release's page in the browser and does not download or replace itself (#131).
/// </summary>
/// <param name="current">This app's version.</param>
/// <param name="kind">How this app was installed: which release asset it takes.</param>
/// <param name="folder">The folder the .msi is downloaded to (Msi only).</param>
/// <param name="restart">Runs the given .msi and closes this app (Msi only).</param>
/// <param name="openPage">Opens a release page in the browser (portable only).</param>
/// <param name="settings">Whether to check at start and to offer betas (#126); the defaults when null.</param>
public sealed class UpdateViewModel(
    HttpClient http, string current, UpdateKind kind, string folder, Action<string> restart, Action<Uri> openPage, EventLog log,
    TimeSpan timeout, SettingsViewModel? settings = null)
    : ObservableObject
{
    private readonly SettingsViewModel settings = settings ?? new SettingsViewModel();

    // A whole update download (about 80 MB): ends one that stalls, leaves time for a slow line.
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(10);

    private UpdateCheckResult? found;
    private bool isOffered;
    private bool isBusy;
    private string? status;
    private string message = "";
    private string closeText = Strings.UpdateSkip;

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

    public string Message
    {
        get => message;
        private set => SetField(ref message, value);
    }

    /// <summary>"Skip" while releases are offered, else "OK".</summary>
    public string CloseText
    {
        get => closeText;
        private set => SetField(ref closeText, value);
    }

    /// <summary>The last check started from the menu.</summary>
    public Task Checking { get; private set; } = Task.CompletedTask;

    /// <summary>Help > Check for updates.</summary>
    public RelayCommand CheckNow => new(() => Checking = CheckAsync(fromMenu: true));

    /// <summary>The stable choice's text, or null when no newer stable release exists.</summary>
    public string? StableText => found?.Stable is { } stable ? string.Format(CultureInfo.CurrentCulture, Strings.UpdateToStable, stable.Version) : null;

    /// <summary>The beta choice's text, or null when no newer beta exists.</summary>
    public string? BetaText => found?.Beta is { } beta ? string.Format(CultureInfo.CurrentCulture, Strings.UpdateToBeta, beta.Version) : null;

    /// <summary>The portable app's hint that a choice opens the release page; null for an .msi install or with nothing to choose.</summary>
    public string? Note => kind == UpdateKind.Portable && (found?.Stable != null || found?.Beta != null) ? Strings.UpdatePortableNote : null;

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
        if (found?.Stable != null || found?.Beta != null)
        {
            log.Add("Update skipped");
        }
    });

    /// <summary>The check after the window opens, unless it is turned off in the settings (#126).</summary>
    public Task CheckAtStartAsync()
    {
        if (!settings.CheckForUpdatesAtStart)
        {
            log.Add("Update check at start is off in the settings");
            return Task.CompletedTask;
        }
        return CheckAsync();
    }

    /// <summary>
    /// Asks GitHub for newer releases; shows the notice if there are any, or, from the menu, also when there are none or
    /// the check failed. Never throws.
    /// </summary>
    public async Task CheckAsync(bool fromMenu = false)
    {
        if (IsBusy)
        {
            return; // a download runs; its notice stays
        }
        UpdateCheckResult result;
        try
        {
            result = await UpdateCheck.FindAsync(http, current, timeout, kind);
        }
        catch (Exception e)
        {
            // The check handles every expected failure; anything else must not reach the window either.
            result = new UpdateCheckResult(null, null, $"Update check skipped: {e.Message}", e.Message);
        }
        log.Add(result.Log);
        if (!settings.OfferBetas && result.Beta != null)
        {
            result = result with { Beta = null };
            log.Add("Betas are off in the settings: the beta is not offered");
        }
        var newer = result.Stable != null || result.Beta != null;
        if (!newer && !fromMenu)
        {
            return; // nothing to show; a notice the menu's check put up meanwhile stays
        }
        found = result;
        OnPropertyChanged(nameof(StableText));
        OnPropertyChanged(nameof(BetaText));
        OnPropertyChanged(nameof(Note));
        Status = null;
        CloseText = newer ? Strings.UpdateSkip : Strings.UpdateClose;
        Message = newer ? string.Format(CultureInfo.CurrentCulture, Strings.UpdateAvailable, current)
            : result.SkippedBecause is { } reason ? string.Format(CultureInfo.CurrentCulture, Strings.UpdateCheckFailed, reason)
            : string.Format(CultureInfo.CurrentCulture, Strings.UpdateUpToDate, current);
        IsOffered = newer || fromMenu;
    }

    /// <summary>
    /// The picked release. Portable: opens its page and closes the popup. Msi: downloads, verifies and runs it; a failure
    /// is shown in <see cref="Status"/> and this app keeps running.
    /// </summary>
    public async Task InstallAsync(bool stable)
    {
        if (IsBusy || (stable ? found?.Stable : found?.Beta) is not { } release)
        {
            return;
        }
        if (kind == UpdateKind.Portable)
        {
            openPage(release.Page);
            log.Add($"Opened the release page of {release.Version}");
            IsOffered = false;
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
            // Only this update's .msi stays: an earlier one (about 80 MB) is no longer needed once it installed.
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
            Directory.CreateDirectory(folder);
            var msi = Path.Combine(folder, release.Download.Segments[^1]);
            await VerifiedDownload.DownloadAsync(http, release, msi, ((IProgress<int>)progress).Report, cancel.Token);
            log.Add($"Downloaded {release.Version} to {msi}; installing it");
            restart(msi);
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
