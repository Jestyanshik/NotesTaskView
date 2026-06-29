using System.Globalization;

namespace NotesTaskView.Services;

public sealed class UserSettings
{
    public string OverlayTitle { get; set; } = "Мои заметки";

    public string NotesDirectory { get; set; } = @"D:\Notes";

    public string ToggleOverlayHotkey { get; set; } = "Ctrl+Shift+Space";

    public string NewNoteHotkey { get; set; } = "Ctrl+Shift+N";

    public string Language { get; set; } = GetDefaultLanguage();

    public bool IsOnboardingComplete { get; set; }

    public double OverlayDimOpacity { get; set; } = 0.75;

    public string SelectionOutlineColor { get; set; } = "#80FFFFFF";

    public string AccentColor { get; set; } = "#FF7AA2FF";

    public bool UseAccentForSelectionOutline { get; set; } = false;

    private static string GetDefaultLanguage()
    {
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ru", StringComparison.OrdinalIgnoreCase)
            ? "ru"
            : "en";
    }
}
