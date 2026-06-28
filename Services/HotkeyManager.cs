using System.Windows.Input;
using System.Windows.Interop;
using NotesTaskView.Interop;

namespace NotesTaskView.Services;

public sealed class HotkeyManager : IDisposable
{
    public const int ToggleWindowHotkeyId = 1001;
    public const int CreateNoteHotkeyId = 1002;

    private readonly HwndSource _source;
    private bool _disposed;

    public HotkeyManager()
    {
        // A tiny hidden native window is enough to receive WM_HOTKEY globally.
        var parameters = new HwndSourceParameters("NotesTaskViewHotkeys")
        {
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
            WindowStyle = 0x800000
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;

    public List<string> Register(UserSettings settings)
    {
        var errors = new List<string>();
        UnregisterAll();

        if (!TryRegister(ToggleWindowHotkeyId, settings.ToggleOverlayHotkey, "горячую клавишу overlay", errors))
        {
            NativeMethods.UnregisterHotKey(_source.Handle, ToggleWindowHotkeyId);
        }

        if (!TryRegister(CreateNoteHotkeyId, settings.NewNoteHotkey, "горячую клавишу новой заметки", errors))
        {
            NativeMethods.UnregisterHotKey(_source.Handle, CreateNoteHotkeyId);
        }

        return errors;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        UnregisterAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
        _disposed = true;
    }

    private bool TryRegister(int id, string hotkeyText, string description, List<string> errors)
    {
        if (!HotkeyGesture.TryParse(hotkeyText, out var gesture, out var parseError))
        {
            errors.Add($"Не удалось зарегистрировать {description} \"{hotkeyText}\": {parseError}");
            return false;
        }

        if (RegisterHotkey(id, gesture.Modifiers, gesture.Key))
        {
            return true;
        }

        errors.Add($"Не удалось зарегистрировать {description} {gesture.DisplayName}. Возможно, она уже занята.");
        return false;
    }

    private void UnregisterAll()
    {
        NativeMethods.UnregisterHotKey(_source.Handle, ToggleWindowHotkeyId);
        NativeMethods.UnregisterHotKey(_source.Handle, CreateNoteHotkeyId);
    }

    private bool RegisterHotkey(int id, uint modifiers, Key key)
    {
        return NativeMethods.RegisterHotKey(
            _source.Handle,
            id,
            modifiers,
            (uint)KeyInterop.VirtualKeyFromKey(key));
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            // The hotkey id lets the app route each shortcut to the right action.
            HotkeyPressed?.Invoke(this, new HotkeyPressedEventArgs
            {
                HotkeyId = wParam.ToInt32()
            });

            handled = true;
        }

        return IntPtr.Zero;
    }
}
