namespace NotesTaskView.Services;

public static class UiText
{
    public const string Ru = "ru";
    public const string En = "en";

    public static string NormalizeLanguage(string? language)
    {
        return string.Equals(language, Ru, StringComparison.OrdinalIgnoreCase) ? Ru : En;
    }

    public static string T(string? language, string key)
    {
        var isRu = NormalizeLanguage(language) == Ru;
        return key switch
        {
            "Settings" => isRu ? "Настройки" : "Settings",
            "Open" => isRu ? "Открыть" : "Open",
            "OnboardingTitle" => isRu ? "Первый запуск" : "First setup",
            "OnboardingIntro" => isRu
                ? "Выберите язык, папку заметок и рабочие горячие клавиши. Дефолты можно изменить."
                : "Choose language, notes folder, and working shortcuts. Defaults can be changed.",
            "Language" => isRu ? "Язык" : "Language",
            "OverlayTitle" => isRu ? "Название overlay" : "Overlay title",
            "NotesFolder" => isRu ? "Папка заметок" : "Notes folder",
            "ConfigFolder" => isRu ? "Папка конфигурации" : "Config folder",
            "ChooseFolder" => isRu ? "Выбрать папку" : "Choose folder",
            "CreateDefaultFolder" => isRu ? "Создать папку по умолчанию" : "Create default folder",
            "FindAutomatically" => isRu ? "Найти автоматически" : "Find automatically",
            "OverlayHotkey" => isRu ? "Горячая клавиша overlay" : "Overlay shortcut",
            "NewNoteHotkey" => isRu ? "Горячая клавиша новой заметки" : "New note shortcut",
            "CheckHotkeys" => isRu ? "Проверить горячие клавиши" : "Check shortcuts",
            "DimOpacity" => isRu ? "Затемнение фона" : "Background dim",
            "AccentColor" => isRu ? "Акцентный цвет" : "Accent color",
            "SelectionColor" => isRu ? "Цвет рамки выделения" : "Selection outline color",
            "UseAccentForSelection" => isRu ? "Использовать accent color для рамки выделения" : "Use accent color for selection outline",
            "Main" => isRu ? "Основные" : "General",
            "Trash" => isRu ? "Мусорка" : "Trash",
            "Cancel" => isRu ? "Отмена" : "Cancel",
            "Save" => isRu ? "Сохранить" : "Save",
            "Done" => isRu ? "Готово" : "Done",
            "Palette" => isRu ? "Палитра" : "Palette",
            "InvalidHex" => isRu ? "Неверный Hex" : "Invalid Hex",
            "SettingsSaved" => isRu ? "Настройки сохранены." : "Settings saved.",
            "HotkeyBusy" => isRu ? "Комбинация уже занята другой программой. Выберите другую." : "This shortcut is already used by another app. Choose another one.",
            "InvalidHotkey" => isRu ? "Неверный формат горячей клавиши. Например: Ctrl+Shift+Space." : "Invalid shortcut format. Example: Ctrl+Shift+Space.",
            "HotkeyOk" => isRu ? "Горячие клавиши свободны." : "Shortcuts are available.",
            "HotkeyUnavailable" => isRu ? "Горячие клавиши пока недоступны." : "Shortcuts are not available yet.",
            "FolderMissingTitle" => isRu ? "Папка заметок не найдена" : "Notes folder not found",
            "ConfigFolderMissingTitle" => isRu ? "Папка конфигурации не найдена" : "Config folder not found",
            "FailedToOpenFolder" => isRu ? "Не удалось открыть папку" : "Failed to open folder",
            "FolderMissingMessage" => isRu
                ? "Сохранённая папка заметок не найдена. Заметки не удалены: выберите старую папку, найдите её автоматически или создайте новую."
                : "The saved notes folder was not found. Notes were not deleted: choose the old folder, find it automatically, or create a new one.",
            "OpenSettings" => isRu ? "Открыть настройки" : "Open settings",
            "EnterFolderPath" => isRu ? "Введите полный путь к папке с заметками." : "Enter the full path to your notes folder.",
            "FolderNotFound" => isRu ? "Такая папка не найдена." : "That folder was not found.",
            "SearchProgress" => isRu ? "Ищем папки с заметками..." : "Searching for notes folders...",
            "SearchEmpty" => isRu ? "Подходящие папки с заметками не найдены в пользовательских папках." : "No matching notes folders were found in user folders.",
            "SearchTitle" => isRu ? "Найдены папки с заметками" : "Notes folders found",
            "SearchMessage" => isRu ? "Выберите папку, которую нужно использовать." : "Choose the folder to use.",
            "Back" => isRu ? "Назад" : "Back",
            "CloseApp" => isRu ? "Закрыть приложение" : "Close app",
            "CloseWithoutSaving" => isRu ? "Закрыть приложение без сохранения изменений?" : "Close the app without saving changes?",
            "Close" => isRu ? "Закрыть" : "Close",
            "Exit" => isRu ? "Выход" : "Exit",
            _ => key
        };
    }
}
