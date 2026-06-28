using System.IO;
using System.Text.Json;

namespace NotesTaskView.Services;

public sealed class UserSettingsService
{
    private const string SettingsFileName = "user-settings.json";
    private readonly string _settingsPath = Path.Combine(AppContext.BaseDirectory, SettingsFileName);

    public UserSettings Load(AppConfig appConfig)
    {
        var settings = CreateDefaultSettings(appConfig);

        if (!File.Exists(_settingsPath))
        {
            Save(settings);
            return settings;
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            var loaded = JsonSerializer.Deserialize<UserSettings>(json);
            if (loaded is null)
            {
                BackupBrokenSettings();
                Save(settings);
                return settings;
            }

            NormalizeSettings(loaded, settings);
            return loaded;
        }
        catch
        {
            BackupBrokenSettings();
            Save(settings);
            return settings;
        }
    }

    public void Save(UserSettings settings)
    {
        var defaults = new UserSettings();
        NormalizeSettings(settings, defaults);

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(_settingsPath, json);
    }

    private static UserSettings CreateDefaultSettings(AppConfig appConfig)
    {
        return new UserSettings
        {
            NotesDirectory = appConfig.NotesFolderPath
        };
    }

    private static void NormalizeSettings(UserSettings settings, UserSettings defaults)
    {
        settings.NotesDirectory = string.IsNullOrWhiteSpace(settings.NotesDirectory)
            ? defaults.NotesDirectory
            : Environment.ExpandEnvironmentVariables(settings.NotesDirectory.Trim());
        settings.OverlayTitle = string.IsNullOrWhiteSpace(settings.OverlayTitle)
            ? defaults.OverlayTitle
            : settings.OverlayTitle.Trim();
        settings.ToggleOverlayHotkey = IsHotkey(settings.ToggleOverlayHotkey)
            ? settings.ToggleOverlayHotkey.Trim()
            : defaults.ToggleOverlayHotkey;
        settings.NewNoteHotkey = IsHotkey(settings.NewNoteHotkey)
            ? settings.NewNoteHotkey.Trim()
            : defaults.NewNoteHotkey;
        settings.OverlayDimOpacity = double.IsFinite(settings.OverlayDimOpacity)
            ? Math.Clamp(settings.OverlayDimOpacity, 0.00, 1.00)
            : defaults.OverlayDimOpacity;
        settings.SelectionOutlineColor = IsHexColor(settings.SelectionOutlineColor)
            ? settings.SelectionOutlineColor.Trim()
            : defaults.SelectionOutlineColor;
        settings.AccentColor = IsHexColor(settings.AccentColor)
            ? settings.AccentColor.Trim()
            : defaults.AccentColor;
    }

    private void BackupBrokenSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return;
            }

            var brokenPath = Path.Combine(
                Path.GetDirectoryName(_settingsPath)!,
                $"user-settings.broken.{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Move(_settingsPath, brokenPath, true);
        }
        catch
        {
            // Loading must fall back to defaults even if backup fails.
        }
    }

    private static bool IsHotkey(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && HotkeyGesture.TryParse(value, out _, out _);
    }

    private static bool IsHexColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var color = value.Trim();
        if (color.Length != 7 && color.Length != 9)
        {
            return false;
        }

        return color[0] == '#' && color[1..].All(Uri.IsHexDigit);
    }
}
