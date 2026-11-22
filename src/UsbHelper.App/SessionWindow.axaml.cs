using Avalonia.Controls;
using Avalonia.Interactivity;
using UsbHelper.App.Resources;
using UsbHelper.App.ViewModels;

namespace UsbHelper.App;

/// <summary>What the session popup's user chose (#341).</summary>
public enum SessionChoice
{
    /// <summary>"Keep this session" or "Later", also when the popup is closed.</summary>
    Keep,

    /// <summary>"New session".</summary>
    NewSession,

    /// <summary>"New session" with "Save the log first" checked.</summary>
    SaveThenNewSession,

    /// <summary>"Restart".</summary>
    Restart,
}

/// <summary>The session popup (#341), modal over the main window. The dialog result is a <see cref="SessionChoice"/>.</summary>
public partial class SessionWindow : Window
{
    private readonly bool restart;

    public SessionWindow()
        : this(new SessionOffer(new Core.Diagnostics.SessionBreak(Core.Diagnostics.SessionBreakKind.Sleep, TimeSpan.Zero, DateTimeOffset.Now), "", false))
    {
    }

    public SessionWindow(SessionOffer offer)
    {
        InitializeComponent();
        restart = offer.Restart;
        Question.Text = offer.Text;
        SaveFirst.IsVisible = !restart;
        Primary.Content = restart ? Strings.SessionRestart : Strings.SessionNew;
        Secondary.Content = restart ? Strings.SessionLater : Strings.SessionKeep;
    }

    private void PrimaryClick(object? sender, RoutedEventArgs e) =>
        Close(restart ? SessionChoice.Restart : SaveFirst.IsChecked == true ? SessionChoice.SaveThenNewSession : SessionChoice.NewSession);

    private void SecondaryClick(object? sender, RoutedEventArgs e) => Close(SessionChoice.Keep);
}
