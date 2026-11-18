using System.Globalization;
using UsbHelper.App.Resources;
using UsbHelper.App.Settings;
using UsbHelper.Core.Database;

namespace UsbHelper.App.ViewModels;

/// <summary>
/// The Custom editor (#300): custom.yaml as text, a blank to download, and Save, which writes the file only when it is a
/// valid guide and then uses it.
/// </summary>
public sealed class CustomGuideViewModel : ObservableObject
{
    private readonly HardwareDatabase database;
    private readonly CustomGuideFile file;
    private readonly Action<Guide> saved;
    private string text;
    private IReadOnlyList<string> errors = [];
    private string? message;

    /// <param name="madeBy">The unlock email, recorded in the blank; null when there is none.</param>
    /// <param name="saved">Called with the guide after a valid text is written.</param>
    public CustomGuideViewModel(HardwareDatabase database, CustomGuideFile file, string? madeBy, Action<Guide> saved)
    {
        this.database = database;
        this.file = file;
        this.saved = saved;
        Blank = CustomGuide.Blank(database, madeBy);
        string? existing = null;
        try
        {
            existing = file.Read();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            message = string.Format(CultureInfo.CurrentCulture, Strings.CustomGuideNotRead, e.Message);
        }
        text = existing ?? Blank;
        SaveCommand = new RelayCommand(Save);
    }

    /// <summary>The blank template: every platform and chip, every value empty. Download blank YAML... saves it.</summary>
    public string Blank { get; }

    /// <summary>The YAML in the editor: the saved custom.yaml, else the blank.</summary>
    public string Text
    {
        get => text;
        set => SetField(ref text, value ?? "");
    }

    /// <summary>The problems Save found, each with its line in custom.yaml; empty when there are none.</summary>
    public IReadOnlyList<string> Errors
    {
        get => errors;
        private set
        {
            if (SetField(ref errors, value))
            {
                OnPropertyChanged(nameof(HasErrors));
            }
        }
    }

    public bool HasErrors => Errors.Count > 0;

    /// <summary>What the last action did: saved, not saved and why; null before any.</summary>
    public string? Message
    {
        get => message;
        set => SetField(ref message, value);
    }

    public RelayCommand SaveCommand { get; }

    private void Save()
    {
        Guide guide;
        try
        {
            guide = CustomGuide.Load(database, Text);
        }
        catch (DatabaseException e)
        {
            Errors = [.. e.Errors.Select(error => error.ToString())];
            Message = Strings.CustomGuideHasErrors;
            return;
        }
        try
        {
            file.Save(Text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Errors = [];
            Message = string.Format(CultureInfo.CurrentCulture, Strings.CustomGuideNotSaved, e.Message);
            return;
        }
        Errors = [];
        Message = Strings.CustomGuideSaved;
        saved(guide);
    }
}
