namespace NotesTaskView.Services;

public sealed class UserSettings
{
    public string OverlayTitle { get; set; } = "Мои заметки";

    public string NotesDirectory { get; set; } = @"D:\Notes";

    public string ToggleOverlayHotkey { get; set; } = "Win+Shift+Tab";

    public string NewNoteHotkey { get; set; } = "Ctrl+Alt+N";

    public double OverlayDimOpacity { get; set; } = 0.75;

    public string SelectionOutlineColor { get; set; } = "#80FFFFFF";

    public string AccentColor { get; set; } = "#FF7AA2FF";

    public bool UseAccentForSelectionOutline { get; set; } = false;
}
