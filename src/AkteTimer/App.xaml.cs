using System;
using System.Threading.Tasks;
using System.Windows;
using AkteTimer.Services;
using AkteTimer.Views;
using MessageBox = System.Windows.MessageBox;

namespace AkteTimer;

public partial class App : System.Windows.Application
{
    private TrayService? _trayService;
    private HotkeyService? _hotkeyService;
    private DatabaseService? _databaseService;
    private TimeEntryService? _timeEntryService;
    private PopupWindow? _popupWindow;
    private SettingsService? _settingsService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        LogService.Initialize();
        RegisterGlobalExceptionHandlers();
        LogService.LogInfo("App-Start.");

        _databaseService = new DatabaseService();
        _databaseService.Initialize();

        _settingsService = new SettingsService(_databaseService);
        _settingsService.EnsureDefaults();

        _timeEntryService = new TimeEntryService(_databaseService, _settingsService);

        _popupWindow = new PopupWindow(_timeEntryService, _settingsService);

        _hotkeyService = new HotkeyService(_settingsService);
        _hotkeyService.HotkeyPressed += (_, _) =>
        {
            _popupWindow.ToggleVisibility();
            _popupWindow.FocusInput();
        };
        _hotkeyService.Register();

        _trayService = new TrayService(_popupWindow, _timeEntryService, _settingsService, _hotkeyService);
        _trayService.Initialize();

        HandleRecovery();
    }

    private static void RegisterGlobalExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                LogService.LogException(exception, "UnhandledException");
                return;
            }

            LogService.LogError("UnhandledException ohne Exception-Objekt.");
        };

        if (System.Windows.Application.Current != null)
        {
            System.Windows.Application.Current.DispatcherUnhandledException += (_, args) =>
            {
                LogService.LogException(args.Exception, "DispatcherUnhandledException");
            };
        }

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogService.LogException(args.Exception, "UnobservedTaskException");
        };
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

        _timeEntryService.StopRunningEntries(DateTime.UtcNow);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyService?.Dispose();
        _trayService?.Dispose();
        LogService.LogInfo("App-Ende.");
        base.OnExit(e);
    }
}
