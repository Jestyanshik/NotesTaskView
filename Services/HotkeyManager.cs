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

        TryRegister(ToggleWindowHotkeyId, settings.ToggleOverlayHotkey, "overlay", errors);
        TryRegister(CreateNoteHotkeyId, settings.NewNoteHotkey, "new-note", errors);

        return errors;
    }

    public void UnregisterAll()
    {
        NativeMethods.UnregisterHotKey(_source.Handle, ToggleWindowHotkeyId);
        NativeMethods.UnregisterHotKey(_source.Handle, CreateNoteHotkeyId);
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

    private bool TryRegister(int id, string hotkeyText, string field, List<string> errors)
    {
        if (!HotkeyGesture.TryParse(hotkeyText, out var gesture, out var parseError))
        {
            errors.Add($"{field}|parse|{parseError}");
            return false;
        }

        if (NativeMethods.RegisterHotKey(
            _source.Handle,
            id,
            gesture.Modifiers,
            (uint)KeyInterop.VirtualKeyFromKey(gesture.Key)))
        {
            return true;
        }

        errors.Add($"{field}|busy|{gesture.DisplayName}");
        return false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            HotkeyPressed?.Invoke(this, new HotkeyPressedEventArgs
            {
                HotkeyId = wParam.ToInt32()
            });

            handled = true;
        }

        return IntPtr.Zero;
    }
}
