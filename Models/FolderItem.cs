using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NotesTaskView.Models;

public sealed class FolderItem : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isFocused;
    private bool _isDragging;
    private bool _isDropTarget;

    public event PropertyChangedEventHandler? PropertyChanged;

    public required string Name { get; init; }

    public required string FullPath { get; init; }

    public required string RelativePath { get; init; }

    public required DateTime LastModified { get; init; }

    public required int ItemCount { get; init; }

    public string DisplayModified => LastModified.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public string DisplayItemCount => ItemCount switch
    {
        0 => "Папка пустая",
        1 => "1 элемент",
        > 1 and < 5 => $"{ItemCount} элемента",
        _ => $"{ItemCount} элементов"
    };

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

    public bool IsDropTarget
    {
        get => _isDropTarget;
        set
        {
            if (_isDropTarget == value)
            {
                return;
            }

            _isDropTarget = value;
            OnPropertyChanged();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
