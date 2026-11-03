namespace UsbHelper.App.ViewModels;

/// <summary>What the main window's commands need from the view: file pickers, windows and closing the app.</summary>
public interface IMainWindowActions
{
    /// <summary>Asks where to save this computer's profile, then writes it.</summary>
    void ExportProfile();

    /// <summary>Asks where to save the log, then writes it.</summary>
    void ExportLog();

    void ShowSettings();

    void ShowAbout();

    void Exit();

    /// <summary>Takes a screenshot of the window, asks where to save the full setup export, then writes it.</summary>
    void ExportSetup();

    /// <summary>Puts <paramref name="text"/> on the clipboard.</summary>
    void CopyText(string text);
}
