using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NotesTaskView.Models;
using NotesTaskView.Services;

namespace NotesTaskView.Views;

public partial class SettingsWindow : Window
{
    private readonly NoteService _noteService;
    private TaskCompletionSource<bool>? _confirmCompletion;

    public SettingsWindow(UserSettings settings, NoteService noteService)
    {
        _noteService = noteService;
        TrashItems = new ObservableCollection<TrashItem>();
        InitializeComponent();
        DataContext = this;

        Settings = new UserSettings
        {
            OverlayTitle = settings.OverlayTitle,
            NotesDirectory = settings.NotesDirectory,
            ToggleOverlayHotkey = settings.ToggleOverlayHotkey,
            NewNoteHotkey = settings.NewNoteHotkey,
            OverlayDimOpacity = settings.OverlayDimOpacity,
            SelectionOutlineColor = settings.SelectionOutlineColor
        };

        OverlayTitleTextBox.Text = Settings.OverlayTitle;
        NotesDirectoryTextBox.Text = Settings.NotesDirectory;
        ToggleHotkeyTextBox.Text = Settings.ToggleOverlayHotkey;
        NewNoteHotkeyTextBox.Text = Settings.NewNoteHotkey;
        DimOpacitySlider.Value = Settings.OverlayDimOpacity;
        SelectionOutlineColorTextBox.Text = Settings.SelectionOutlineColor;
        Loaded += async (_, _) => await RefreshTrashAsync();
    }

    public UserSettings Settings { get; private set; }

    public ObservableCollection<TrashItem> TrashItems { get; }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!ValidateHotkey(ToggleHotkeyTextBox.Text, "горячую клавишу overlay") ||
            !ValidateHotkey(NewNoteHotkeyTextBox.Text, "горячую клавишу новой заметки") ||
            !ValidateHexColor(SelectionOutlineColorTextBox.Text))
        {
            return;
        }

        Settings = new UserSettings
        {
            OverlayTitle = string.IsNullOrWhiteSpace(OverlayTitleTextBox.Text) ? "Мои заметки" : OverlayTitleTextBox.Text.Trim(),
            NotesDirectory = NotesDirectoryTextBox.Text,
            ToggleOverlayHotkey = ToggleHotkeyTextBox.Text,
            NewNoteHotkey = NewNoteHotkeyTextBox.Text,
            OverlayDimOpacity = DimOpacitySlider.Value,
            SelectionOutlineColor = SelectionOutlineColorTextBox.Text.Trim()
        };

        DialogResult = true;
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private async void RestoreTrashButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not TrashItem item)
        {
            return;
        }

        var result = await _noteService.RestoreTrashItemAsync(item);
        ShowOperation(result);
        await RefreshTrashAsync();
    }

    private async void DeleteForeverButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not TrashItem item)
        {
            return;
        }

        var confirmed = await ShowConfirmAsync(
            "Мусорка",
            "Удалить заметку навсегда?",
            "Удалить");

        if (!confirmed)
        {
            return;
        }

        var result = await _noteService.DeleteTrashItemPermanentlyAsync(item);
        ShowOperation(result);
        await RefreshTrashAsync();
    }

    private async void EmptyTrashButton_OnClick(object sender, RoutedEventArgs e)
    {
        var confirmed = await ShowConfirmAsync(
            "Мусорка",
            "Очистить мусорку?",
            "Очистить");

        if (!confirmed)
        {
            return;
        }

        var result = await _noteService.EmptyTrashAsync();
        ShowOperation(result);
        await RefreshTrashAsync();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (SettingsConfirmOverlay.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Escape)
            {
                CompleteConfirm(false);
                e.Handled = true;
            }

            return;
        }

        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }

    private async Task RefreshTrashAsync()
    {
        TrashItems.Clear();
        foreach (var item in await _noteService.GetTrashItemsAsync())
        {
            TrashItems.Add(item);
        }
    }

    private bool ValidateHotkey(string text, string label)
    {
        if (HotkeyGesture.TryParse(text, out _, out var error))
        {
            return true;
        }

        ShowStatus($"Не удалось распознать {label}: {error}");
        return false;
    }

    private bool ValidateHexColor(string text)
    {
        try
        {
            _ = ColorConverter.ConvertFromString(text.Trim());
            return true;
        }
        catch
        {
            ShowStatus("Цвет рамки выделения должен быть в формате #RRGGBB или #AARRGGBB.");
            return false;
        }
    }

    private void ShowOperation(NoteOperationResult result)
    {
        ShowStatus(result.Message);
    }

    private Task<bool> ShowConfirmAsync(string title, string message, string confirmText)
    {
        _confirmCompletion = new TaskCompletionSource<bool>();
        SettingsConfirmTitleText.Text = title;
        SettingsConfirmMessageText.Text = message;
        SettingsConfirmOkButton.Content = confirmText;
        SettingsConfirmOverlay.Visibility = Visibility.Visible;
        return _confirmCompletion.Task;
    }

    private void SettingsConfirmOkButton_OnClick(object sender, RoutedEventArgs e)
    {
        CompleteConfirm(true);
    }

    private void SettingsConfirmCancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        CompleteConfirm(false);
    }

    private void CompleteConfirm(bool result)
    {
        SettingsConfirmOverlay.Visibility = Visibility.Collapsed;
        _confirmCompletion?.TrySetResult(result);
        _confirmCompletion = null;
    }

    private void ShowStatus(string message)
    {
        SettingsStatusText.Text = message;
        SettingsStatusBorder.Visibility = Visibility.Visible;
    }
}
