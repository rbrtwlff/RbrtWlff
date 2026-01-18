using System.Drawing;
using System.Windows.Forms;
using AkteTimer.Views;

namespace AkteTimer.Services;

public sealed class TrayService : IDisposable
{
    private readonly PopupWindow _popupWindow;
    private readonly TimeEntryService _timeEntryService;
    private readonly SettingsService _settingsService;
    private readonly HotkeyService _hotkeyService;
    private NotifyIcon? _notifyIcon;
    private TodayWindow? _todayWindow;
    private ReportsWindow? _reportsWindow;
    private SettingsWindow? _settingsWindow;

    public TrayService(PopupWindow popupWindow, TimeEntryService timeEntryService, SettingsService settingsService, HotkeyService hotkeyService)
    {
        _popupWindow = popupWindow;
        _timeEntryService = timeEntryService;
        _settingsService = settingsService;
        _hotkeyService = hotkeyService;
    }

    public void Initialize()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "AkteTimer"
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Öffnen", null, (_, _) => RunOnUiThread(ShowPopup));
        menu.Items.Add("Start/Pause", null, (_, _) => RunOnUiThread(() => _timeEntryService.ToggleStartPause()));
        menu.Items.Add("Heute", null, (_, _) => RunOnUiThread(ShowToday));
        menu.Items.Add("Auswertung", null, (_, _) => RunOnUiThread(ShowReports));
        menu.Items.Add("Einstellungen", null, (_, _) => RunOnUiThread(ShowSettings));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) => Exit());

        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.MouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left)
            {
                RunOnUiThread(ShowPopup);
            }
        };
    }

    public void Dispose()
    {
        _notifyIcon?.Dispose();
    }

    private void ShowPopup()
    {
        _popupWindow.ToggleVisibility();
    }

    private void ShowToday()
    {
        _todayWindow ??= new TodayWindow(_timeEntryService);
        _todayWindow.Show();
        _todayWindow.Activate();
    }

    private void ShowReports()
    {
        _reportsWindow ??= new ReportsWindow(_timeEntryService);
        _reportsWindow.Show();
        _reportsWindow.Activate();
    }

    private void ShowSettings()
    {
        _settingsWindow ??= new SettingsWindow(_settingsService, _hotkeyService);
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void Exit()
    {
        RunOnUiThread(() =>
        {
            _popupWindow.PrepareForShutdown();
            System.Windows.Application.Current.Shutdown();
        });
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }
}
