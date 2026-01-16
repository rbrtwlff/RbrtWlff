using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using AkteTimer.Views;

namespace AkteTimer.Services;

public sealed class TrayService : IDisposable
{
    private readonly PopupWindow _popupWindow;
    private readonly TimeEntryService _timeEntryService;
    private NotifyIcon? _notifyIcon;
    private TodayWindow? _todayWindow;
    private ReportsWindow? _reportsWindow;
    private SettingsWindow? _settingsWindow;

    public TrayService(PopupWindow popupWindow, TimeEntryService timeEntryService)
    {
        _popupWindow = popupWindow;
        _timeEntryService = timeEntryService;
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
        menu.Items.Add("Öffnen", null, (_, _) => ShowPopup());
        menu.Items.Add("Heute", null, (_, _) => ShowToday());
        menu.Items.Add("Auswertung", null, (_, _) => ShowReports());
        menu.Items.Add("Einstellungen", null, (_, _) => ShowSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) => Exit());

        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.MouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left)
            {
                ShowPopup();
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
        _popupWindow.FocusInput();
    }

    private void ShowToday()
    {
        _todayWindow ??= new TodayWindow(_timeEntryService);
        _todayWindow.Show();
        _todayWindow.Activate();
    }

    private void ShowReports()
    {
        _reportsWindow ??= new ReportsWindow();
        _reportsWindow.Show();
        _reportsWindow.Activate();
    }

    private void ShowSettings()
    {
        _settingsWindow ??= new SettingsWindow();
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private static void Exit()
    {
        Application.Current.Shutdown();
    }
}
