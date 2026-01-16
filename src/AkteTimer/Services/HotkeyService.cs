using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace AkteTimer.Services;

public sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 9001;
    private const int WmHotkey = 0x0312;

    private readonly SettingsService _settings;
    private HwndSource? _source;

    public event EventHandler? HotkeyPressed;

    public HotkeyService(SettingsService settings)
    {
        _settings = settings;
    }

    public void Register()
    {
        if (_source != null)
        {
            return;
        }

        var parameters = new HwndSourceParameters("AkteTimerHotkey")
        {
            Width = 0,
            Height = 0,
            WindowStyle = unchecked((int)0x80000000) // WS_POPUP
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        var (modifiers, key) = ParseHotkey(_settings.Hotkey);
        RegisterHotKey(_source.Handle, HotkeyId, modifiers, key);
    }

    public void Dispose()
    {
        if (_source == null)
        {
            return;
        }

        UnregisterHotKey(_source.Handle, HotkeyId);
        _source.RemoveHook(WndProc);
        _source.Dispose();
        _source = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static (uint modifiers, uint key) ParseHotkey(string hotkey)
    {
        var parts = hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        uint modifiers = 0;
        uint key = 0;

        foreach (var part in parts)
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL":
                    modifiers |= 0x0002;
                    break;
                case "ALT":
                    modifiers |= 0x0001;
                    break;
                case "SHIFT":
                    modifiers |= 0x0004;
                    break;
                case "WIN":
                    modifiers |= 0x0008;
                    break;
                default:
                    key = (uint)KeyInterop.VirtualKeyFromKey(Enum.Parse<System.Windows.Input.Key>(part, true));
                    break;
            }
        }

        if (key == 0)
        {
            key = (uint)KeyInterop.VirtualKeyFromKey(System.Windows.Input.Key.T);
        }

        return (modifiers, key);
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
