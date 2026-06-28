namespace NotesTaskView.Services;

public sealed class HotkeyPressedEventArgs : EventArgs
{
    public required int HotkeyId { get; init; }
}
