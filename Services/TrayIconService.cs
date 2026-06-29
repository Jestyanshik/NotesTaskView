using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace NotesTaskView.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Func<Task> _openAppAsync;
    private readonly Func<Task> _openSettingsAsync;
    private readonly Func<Task> _openNotesFolderAsync;
    private readonly Func<Task> _openConfigFolderAsync;
    private readonly Func<Task> _closeAppAsync;
    private readonly Forms.NotifyIcon _notifyIcon;
    private Forms.ContextMenuStrip? _contextMenu;
    private UserSettings _settings;
    private bool _disposed;

    public TrayIconService(
        Dispatcher dispatcher,
        UserSettings settings,
        Func<Task> openAppAsync,
        Func<Task> openSettingsAsync,
        Func<Task> openNotesFolderAsync,
        Func<Task> openConfigFolderAsync,
        Func<Task> closeAppAsync)
    {
        _dispatcher = dispatcher;
        _settings = CloneSettings(settings);
        _openAppAsync = openAppAsync;
        _openSettingsAsync = openSettingsAsync;
        _openNotesFolderAsync = openNotesFolderAsync;
        _openConfigFolderAsync = openConfigFolderAsync;
        _closeAppAsync = closeAppAsync;

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "NotesTaskView",
            Visible = true
        };
        _notifyIcon.MouseClick += NotifyIcon_OnMouseClick;
        RebuildMenu();
    }

    public void UpdateSettings(UserSettings settings)
    {
        _settings = CloneSettings(settings);
        RebuildMenu();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _notifyIcon.MouseClick -= NotifyIcon_OnMouseClick;
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip = null;
        _contextMenu?.Dispose();
        _notifyIcon.Icon?.Dispose();
        _notifyIcon.Dispose();
        _disposed = true;
    }

    private void NotifyIcon_OnMouseClick(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Left)
        {
            RunOnDispatcher(_openAppAsync);
        }
    }

    private void RebuildMenu()
    {
        var oldMenu = _contextMenu;
        var language = _settings.Language;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(CreateMenuItem(UiText.T(language, "Open"), _openAppAsync));
        menu.Items.Add(CreateMenuItem(UiText.T(language, "Settings"), _openSettingsAsync));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(CreateMenuItem(UiText.T(language, "NotesFolder"), _openNotesFolderAsync));
        menu.Items.Add(CreateMenuItem(UiText.T(language, "ConfigFolder"), _openConfigFolderAsync));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(CreateMenuItem(UiText.T(language, "CloseApp"), _closeAppAsync));

        _contextMenu = menu;
        _notifyIcon.ContextMenuStrip = menu;
        oldMenu?.Dispose();
    }

    private Forms.ToolStripMenuItem CreateMenuItem(string text, Func<Task> action)
    {
        var item = new Forms.ToolStripMenuItem(text);
        item.Click += (_, _) => RunOnDispatcher(action);
        return item;
    }

    private void RunOnDispatcher(Func<Task> action)
    {
        if (_disposed)
        {
            return;
        }

        _ = _dispatcher.BeginInvoke(async () =>
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        });
    }

    private static Icon LoadTrayIcon()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
            {
                var extracted = Icon.ExtractAssociatedIcon(exePath);
                if (extracted is not null)
                {
                    return extracted;
                }
            }
        }
        catch
        {
            // Fall back to the stock app icon if the executable icon cannot be read.
        }

        return (Icon)SystemIcons.Application.Clone();
    }

    private static UserSettings CloneSettings(UserSettings settings)
    {
        return new UserSettings
        {
            OverlayTitle = settings.OverlayTitle,
            NotesDirectory = settings.NotesDirectory,
            ToggleOverlayHotkey = settings.ToggleOverlayHotkey,
            NewNoteHotkey = settings.NewNoteHotkey,
            Language = settings.Language,
            IsOnboardingComplete = settings.IsOnboardingComplete,
            OverlayDimOpacity = settings.OverlayDimOpacity,
            SelectionOutlineColor = settings.SelectionOutlineColor,
            AccentColor = settings.AccentColor,
            UseAccentForSelectionOutline = settings.UseAccentForSelectionOutline
        };
    }
}
