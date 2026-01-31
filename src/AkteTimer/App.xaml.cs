using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Markup;
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
    private BillingService? _billingService;
    private PopupWindow? _popupWindow;
    private SettingsService? _settingsService;
    private DataDirectoryService? _dataDirectoryService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ApplyCulture();

        _dataDirectoryService = new DataDirectoryService();
        var startupDirectory = ResolveStartupDirectory(_dataDirectoryService);
        if (startupDirectory == null)
        {
            Shutdown();
            return;
        }

        _dataDirectoryService.SetCurrentDirectory(startupDirectory);
        _dataDirectoryService.PersistDirectory(startupDirectory);

        LogService.Initialize(_dataDirectoryService.LogsDirectory);
        RegisterGlobalExceptionHandlers();
        LogService.LogInfo("App-Start.");

        _databaseService = new DatabaseService(_dataDirectoryService);
        _databaseService.Initialize();

        _settingsService = new SettingsService(_databaseService);
        _settingsService.EnsureDefaults();
        if (!ApplyStoredDataDirectory(_dataDirectoryService, _databaseService, _settingsService))
        {
            Shutdown();
            return;
        }

        _timeEntryService = new TimeEntryService(_databaseService, _settingsService);
        _billingService = new BillingService(_databaseService);

        _popupWindow = new PopupWindow(_timeEntryService, _settingsService);

        _hotkeyService = new HotkeyService(_settingsService);
        _hotkeyService.HotkeyPressed += (_, _) =>
        {
            _popupWindow.ToggleVisibility();
            _popupWindow.FocusInput();
        };
        _hotkeyService.Register();

        _trayService = new TrayService(_popupWindow, _timeEntryService, _settingsService, _hotkeyService, _dataDirectoryService, _databaseService, _billingService);
        _trayService.Initialize();

        HandleRecovery();
    }

    private static void ApplyCulture()
    {
        var culture = CultureInfo.GetCultureInfo("de-DE");
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));
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

        _timeEntryService.Stop();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyService?.Dispose();
        _trayService?.Dispose();
        LogService.LogInfo("App-Ende.");
        base.OnExit(e);
    }

    private static string? ResolveStartupDirectory(DataDirectoryService dataDirectoryService)
    {
        var preferredDirectory = dataDirectoryService.LoadPersistedDirectory() ?? dataDirectoryService.DefaultDirectory;
        if (dataDirectoryService.TryEnsureWritable(preferredDirectory, out _))
        {
            return preferredDirectory;
        }

        return PromptForUnavailableDirectory(dataDirectoryService, preferredDirectory);
    }

    private static string? PromptForUnavailableDirectory(DataDirectoryService dataDirectoryService, string missingDirectory)
    {
        var result = MessageBox.Show(
            $"Der Datenordner ist nicht erreichbar:\n{missingDirectory}\n\n" +
            "Möchten Sie einen anderen Ordner auswählen?",
            "Datenordner nicht erreichbar",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.Yes);

        if (result == MessageBoxResult.No)
        {
            if (dataDirectoryService.TryEnsureWritable(dataDirectoryService.DefaultDirectory, out _))
            {
                return dataDirectoryService.DefaultDirectory;
            }

            MessageBox.Show(
                "Der Standard-Datenordner ist ebenfalls nicht verfügbar. Die App wird beendet.",
                "Datenordner",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return null;
        }

        while (true)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Datenordner auswählen",
                SelectedPath = dataDirectoryService.DefaultDirectory,
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() != DialogResult.OK)
            {
                MessageBox.Show(
                    "Ohne Datenordner kann die App nicht starten.",
                    "Datenordner",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return null;
            }

            if (dataDirectoryService.TryEnsureWritable(dialog.SelectedPath, out var errorMessage))
            {
                return dialog.SelectedPath;
            }

            MessageBox.Show(
                $"Der Ordner konnte nicht beschrieben werden:\n{errorMessage}",
                "Datenordner",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static bool ApplyStoredDataDirectory(DataDirectoryService dataDirectoryService, DatabaseService databaseService, SettingsService settingsService)
    {
        var storedDirectory = settingsService.DataDirectory;
        if (string.IsNullOrWhiteSpace(storedDirectory))
        {
            settingsService.SetDataDirectory(dataDirectoryService.CurrentDirectory);
            dataDirectoryService.PersistDirectory(dataDirectoryService.CurrentDirectory);
            return true;
        }

        if (DataDirectoryService.AreSameDirectory(storedDirectory, dataDirectoryService.CurrentDirectory))
        {
            return true;
        }

        if (!dataDirectoryService.TryEnsureWritable(storedDirectory, out _))
        {
            var fallback = PromptForUnavailableDirectory(dataDirectoryService, storedDirectory);
            if (fallback == null)
            {
                return false;
            }

            storedDirectory = fallback;
        }

        dataDirectoryService.SetCurrentDirectory(storedDirectory);
        dataDirectoryService.PersistDirectory(storedDirectory);
        databaseService.Initialize();
        settingsService.SetDataDirectory(storedDirectory);
        LogService.UpdateLogDirectory(dataDirectoryService.LogsDirectory);
        return true;
    }
}
