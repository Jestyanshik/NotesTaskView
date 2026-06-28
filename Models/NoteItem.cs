using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NotesTaskView.Models;

public sealed class NoteItem : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isFocused;
    private bool _isDragging;

    public event PropertyChangedEventHandler? PropertyChanged;

    public required string Title { get; init; }

    public required string FullPath { get; init; }

    public required DateTime LastModified { get; init; }

    public required long SizeBytes { get; init; }

    public string DisplayModified => LastModified.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public string DisplaySize => FormatBytes(SizeBytes);

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public bool IsFocused
    {
        get => _isFocused;
        set
        {
            if (_isFocused == value)
            {
                return;
            }

            _isFocused = value;
            OnPropertyChanged();
        }
    }

    public bool IsDragging
    {
        get => _isDragging;
        set
        {
            if (_isDragging == value)
            {
                return;
            }

            _isDragging = value;
            OnPropertyChanged();
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB"];
        var size = Math.Max(bytes, 0);
        var order = 0;
        decimal readable = size;

        while (readable >= 1024 && order < suffixes.Length - 1)
        {
            order++;
            readable /= 1024;
        }

        return $"{readable:0.#} {suffixes[order]}";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
