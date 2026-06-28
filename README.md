# NotesTaskView / Notes Overlay

Windows overlay-приложение для заметок в стиле Task View / Steam Overlay. Работает с `.txt` заметками и папками, показывает карточки, поддерживает встроенный редактор, поиск, drag/reorder и кастомные overlay-диалоги без стандартного `MessageBox`.

## Features

- Fullscreen overlay поверх экрана.
- Работа с обычными `.txt` заметками.
- Папки внутри каталога заметок.
- Карточки заметок и папок вместо таблицы.
- Встроенный редактор без запуска Notepad.
- Создание, удаление, перемещение и переименование элементов.
- Drag/reorder карточек.
- Drag-to-parent при перетаскивании из папки.
- Multi-select через `Ctrl`, `Shift`, `Alt`.
- Group actions и group drag.
- Middle mouse hold preview.
- Global search через `Ctrl + F` по названию и содержимому.
- File search через `Ctrl + F` внутри открытого файла.
- Настраиваемые accent color и цвет рамки выделения.
- Кастомные overlay-dialogs без стандартного Windows `ContextMenu`.
- Иконка приложения для exe и ярлыков.

## Requirements

- Windows 10/11 x64.
- Для self-contained release `.NET Runtime` не нужен.
- Для framework-dependent build нужен `.NET Desktop Runtime 8`.
- Для сборки из исходников нужен `.NET SDK 8`.

## Download
- `NotesTaskView-Setup.exe` - recommended installer.
- `NotesTaskView-v1.0.0-win-x64-self-contained.zip` - portable self-contained build.

Self-contained версия не требует установленного .NET Runtime.

## Installation

Через installer:

1. Скачайте `NotesTaskView-Setup.exe` из GitHub Releases.
2. Запустите установщик.
3. Установщик добавит приложение в Start Menu.
4. Установщик может добавить приложение в автозагрузку Windows.
5. На неизвестном unsigned exe Windows SmartScreen может показать предупреждение.

Self-contained exe после publish:

```text
publish\win-x64-self-contained\NotesTaskView.exe
```

Такой exe должен запускаться на чистом Windows 10/11 x64 без установки .NET Runtime.

PowerShell installer:

```powershell
cd E:\notes\Notes-overlay
.\installer\install.ps1
```

С ярлыком на Desktop:

```powershell
.\installer\install.ps1 -DesktopShortcut
```

Installer копирует приложение в:

```text
%LOCALAPPDATA%\NotesTaskView
```

и создаёт ярлыки в Start Menu и `shell:startup`. Admin rights не требуются.

## Notes folder recovery

- Удаление приложения не удаляет пользовательские заметки.
- Обычный uninstall не удаляет `%LocalAppData%\NotesTaskView\settings.json`.
- При переустановке приложение использует старый путь к папке заметок из `settings.json`.
- Если папка заметок не найдена, приложение покажет внутренний overlay-dialog и предложит создать новую папку, выбрать существующую или найти автоматически.
- Автопоиск не запускается сам. Он стартует только после нажатия кнопки пользователем.
- Автопоиск сканирует только безопасные пользовательские папки: Documents, Desktop, UserProfile, LocalApplicationData, ApplicationData и OneDrive, если он настроен.
- Автопоиск не сканирует `C:\Windows`, `C:\Program Files`, `C:\Program Files (x86)` и корни всех дисков без подтверждения.

## Build from source

```powershell
& "C:\Program Files\dotnet\dotnet.exe" restore E:\notes\Notes-overlay\NotesTaskView.csproj
& "C:\Program Files\dotnet\dotnet.exe" build E:\notes\Notes-overlay\NotesTaskView.csproj -c Release
```

Проверочная сборка в отдельную папку:

```powershell
& "C:\Program Files\dotnet\dotnet.exe" build E:\notes\Notes-overlay\NotesTaskView.csproj -c Release -o E:\notes\Notes-overlay\build-check
```

## Publish

Self-contained single-file publish:

```powershell
& "C:\Program Files\dotnet\dotnet.exe" publish E:\notes\Notes-overlay\NotesTaskView.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o E:\notes\Notes-overlay\publish\win-x64-self-contained
```

Framework-dependent publish:

```powershell
& "C:\Program Files\dotnet\dotnet.exe" publish E:\notes\Notes-overlay\NotesTaskView.csproj -c Release -r win-x64 --self-contained false -o E:\notes\Notes-overlay\publish\win-x64-framework-dependent
```

## App icon

Source PNG:

```text
Assets\app-icon.png
```

Application ICO:

```text
Assets\app-icon.ico
```

The icon is connected in `NotesTaskView.csproj`:

```xml
<ApplicationIcon>Assets\app-icon.ico</ApplicationIcon>
```

`app-icon.ico` contains `16x16`, `24x24`, `32x32`, `48x48`, `64x64`, `128x128`, and `256x256` images.

## Autostart

Manual autostart:

1. Press `Win + R`.
2. Enter:

```text
shell:startup
```

3. Add a shortcut to the published exe:

```text
E:\notes\Notes-overlay\publish\win-x64-self-contained\NotesTaskView.exe
```

The PowerShell installer creates this Startup shortcut automatically.

## Shortcuts

Main shortcuts:

- `Win + Shift + Tab` - show or hide overlay.
- `Ctrl + Alt + N` - create a note.
- `Ctrl + S` - save in editor.
- `Ctrl + F` - search in list/editor depending on mode.
- `F5` - refresh.
- `Esc` - back, close overlay panel state, or hide overlay depending on mode.

Full list: [SHORTCUTS.md](./SHORTCUTS.md).

## Settings

Default notes folder is configured in [appsettings.json](./appsettings.json):

```json
{
  "NotesFolderPath": "D:\\Notes"
}
```

Runtime UI settings are stored in:

```text
%LocalAppData%\NotesTaskView\settings.json
```

This file is intentionally ignored by git and survives normal uninstall/reinstall.

Default overlay dim opacity for a new installation is `0.75` / 75%. If `settings.json` already exists, it can override new defaults. To reset defaults, delete `%LocalAppData%\NotesTaskView\settings.json` or change the value in the application settings UI.

## Installer

Installer-related files:

- `installer\install.ps1` - copies the self-contained exe to `%LOCALAPPDATA%\NotesTaskView`, creates Start Menu and Startup shortcuts.
- `installer\uninstall.ps1` - removes shortcuts and installed app files. It keeps `settings.json` by default.
- `installer\NotesTaskView.iss` - Inno Setup script.

Remove user settings explicitly:

```powershell
.\installer\uninstall.ps1 -RemoveUserSettings
```

To build an installer exe from `.iss`, install Inno Setup and compile:

```text
installer\NotesTaskView.iss
```

The installer does not download .NET because the self-contained publish includes the required runtime.

## Known limitations

- WPF version is Windows-only.
- Linux/macOS would require Avalonia or another cross-platform UI.
- Windows SmartScreen may warn about unsigned exe files.
- Existing `user-settings.json` can override new default settings.
- Global hotkeys may fail if another app already owns the same shortcut.

## License

MIT. See [LICENSE](./LICENSE).
