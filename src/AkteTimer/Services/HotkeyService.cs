using System.Runtime.InteropServices;
using System.Windows.Interop;
namespace AkteTimer.Services;

public sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 9001;
    private const int WmHotkey = 0x0312;
    private const uint ModControl = 0x0002;
    private const uint ModAlt = 0x0001;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

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
        if (!RegisterHotKey(_source.Handle, HotkeyId, modifiers, key))
        {
            CleanupSource();
        }
    }

    public void Dispose()
    {
        CleanupSource();
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
                    modifiers |= ModControl;
                    break;
                case "ALT":
                    modifiers |= ModAlt;
                    break;
                case "SHIFT":
                    modifiers |= ModShift;
                    break;
                case "WIN":
                    modifiers |= ModWin;
                    break;
                default:
                    key = (uint)Enum.Parse<System.Windows.Input.Key>(part, true);
                    break;
            }
        }

        if (key == 0)
        {
            key = (uint)System.Windows.Input.Key.T;
        }

        if (modifiers == 0)
        {
            modifiers = ModControl | ModAlt;
        }

        return (modifiers, key);
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private void CleanupSource()
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
}
