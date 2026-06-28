namespace NotesTaskView.Models;

public sealed class TrashItem
{
    public required string DisplayName { get; init; }

    public required string TrashPath { get; init; }

    public required string? MetadataPath { get; init; }

    public required string OriginalPath { get; init; }

    public required DateTime DeletedAt { get; init; }

    public string DisplayDeletedAt => DeletedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}
