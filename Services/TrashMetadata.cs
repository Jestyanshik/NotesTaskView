namespace NotesTaskView.Services;

public sealed class TrashMetadata
{
    public string OriginalPath { get; set; } = string.Empty;

    public string OriginalName { get; set; } = string.Empty;

    public DateTime DeletedAt { get; set; }
}
