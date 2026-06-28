namespace NotesTaskView.Services;

public sealed record NoteOperationResult(bool Success, string Message, string? FilePath = null);
