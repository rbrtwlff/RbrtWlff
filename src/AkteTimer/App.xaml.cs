using System.Windows;
using AkteTimer.Services;
using AkteTimer.Views;

namespace AkteTimer;

public partial class App : Application
{
    private TrayService? _trayService;
    private HotkeyService? _hotkeyService;
    private DatabaseService? _databaseService;
    private TimeEntryService? _timeEntryService;
    private PopupWindow? _popupWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _databaseService = new DatabaseService();
        _databaseService.Initialize();

        var settingsService = new SettingsService(_databaseService);
        settingsService.EnsureDefaults();

        _timeEntryService = new TimeEntryService(_databaseService, settingsService);

        _popupWindow = new PopupWindow(_timeEntryService);

        _trayService = new TrayService(_popupWindow, _timeEntryService);
        _trayService.Initialize();

        _hotkeyService = new HotkeyService(settingsService);
        _hotkeyService.HotkeyPressed += (_, _) =>
        {
            _popupWindow.ToggleVisibility();
            _popupWindow.FocusInput();
        };
        _hotkeyService.Register();

        HandleRecovery();
    }

    private void HandleRecovery()
    {
        if (_timeEntryService == null)
        {
            return;
        }

        var runningEntry = _timeEntryService.GetRunningEntry();
        if (runningEntry == null)
        {
            return;
        }

        var message = "Es läuft ein Zeiteintrag ohne Ende.\n\n" +
                      "Option 1: Beenden um jetzt (Standard)\n" +
                      "Option 2: Fortsetzen\n\n" +
                      "Möchten Sie fortsetzen?";

        var result = MessageBox.Show(message, "Wiederherstellung", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (result == MessageBoxResult.Yes)
        {
            _timeEntryService.ResumeRunningEntry(runningEntry);
            return;
        }

        _timeEntryService.StopRunningEntry(runningEntry, DateTime.UtcNow);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyService?.Dispose();
        _trayService?.Dispose();
        base.OnExit(e);
    }
}
