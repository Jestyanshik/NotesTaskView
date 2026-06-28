namespace NotesTaskView.Services;

public sealed record NoteTextResult(bool Success, string Message, string Content = "");