using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace NotesTaskView.Models;

public sealed class SearchResultItem : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isFocused;
    private bool _isDragging;

    public required string Title { get; init; }

    public required string FullPath { get; init; }

    public required string RelativeFolder { get; init; }

    public required bool IsFolder { get; init; }

    public string Snippet { get; init; } = string.Empty;

    public int? MatchLineNumber { get; init; }

    public int? MatchColumn { get; init; }

    public string MatchBadge => MatchLineNumber is null ? "Найдено в названии" : "Найдено внутри";

    public Visibility FolderIconVisibility => IsFolder ? Visibility.Visible : Visibility.Collapsed;

    public Visibility NoteIconVisibility => IsFolder ? Visibility.Collapsed : Visibility.Visible;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public bool IsFocused
    {
        get => _isFocused;
        set => SetField(ref _isFocused, value);
    }

    public bool IsDragging
    {
        get => _isDragging;
        set => SetField(ref _isDragging, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
