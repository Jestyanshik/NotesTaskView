using System.Windows.Input;
using NotesTaskView.Interop;

namespace NotesTaskView.Services;

public sealed record HotkeyGesture(string DisplayName, uint Modifiers, Key Key)
{
    public static bool TryParse(string value, out HotkeyGesture gesture, out string error)
    {
        gesture = new HotkeyGesture(string.Empty, 0, Key.None);
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Горячая клавиша не задана.";
            return false;
        }

        var parts = value
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (parts.Count < 2)
        {
            error = "Используйте формат вроде Win+Shift+Tab или Ctrl+Alt+N.";
            return false;
        }

        uint modifiers = 0;
        Key key = Key.None;
        var displayParts = new List<string>();

        foreach (var part in parts)
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= NativeMethods.MOD_CONTROL;
                    displayParts.Add("Ctrl");
                    break;
                case "ALT":
                    modifiers |= NativeMethods.MOD_ALT;
                    displayParts.Add("Alt");
                    break;
                case "SHIFT":
                    modifiers |= NativeMethods.MOD_SHIFT;
                    displayParts.Add("Shift");
                    break;
                case "WIN":
                case "WINDOWS":
                    modifiers |= NativeMethods.MOD_WIN;
                    displayParts.Add("Win");
                    break;
                default:
                    if (!Enum.TryParse(part, true, out key) || key == Key.None)
                    {
                        error = $"Не удалось распознать клавишу: {part}.";
                        return false;
                    }

                    displayParts.Add(NormalizeKeyName(key));
                    break;
            }
        }

        if (modifiers == 0 || key == Key.None)
        {
            error = "Горячая клавиша должна содержать модификатор и основную клавишу.";
            return false;
        }

        gesture = new HotkeyGesture(string.Join("+", displayParts), modifiers, key);
        return true;
    }

    private static string NormalizeKeyName(Key key)
    {
        return key switch
        {
            Key.Return => "Enter",
            Key.Escape => "Esc",
            _ => key.ToString()
        };
    }
}
