using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using AkteTimer.Services;
using Microsoft.Data.Sqlite;
using DialogResult = System.Windows.Forms.DialogResult;
using FolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;
using MessageBox = System.Windows.MessageBox;

namespace AkteTimer.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settings;
    private readonly HotkeyService _hotkeyService;
    private readonly DataDirectoryService _dataDirectoryService;
    private readonly DatabaseService _databaseService;
    private string _hotkey;
    private string _globalTargetRateText;
    private bool _hashtagStopPromptEnabled;
    private bool _useLastHashtagDefault;
    private string _tableVersion;
    private string _dataDirectory;
    private bool _isRecording;
    private string _recordHint;

    public SettingsViewModel(SettingsService settings, HotkeyService hotkeyService, DataDirectoryService dataDirectoryService, DatabaseService databaseService)
    {
        _settings = settings;
        _hotkeyService = hotkeyService;
        _dataDirectoryService = dataDirectoryService;
        _databaseService = databaseService;
        _hotkey = _settings.Hotkey;
        _globalTargetRateText = _settings.GlobalTargetRateEurPerHour.ToString("N2", CultureInfo.CurrentCulture);
        _hashtagStopPromptEnabled = _settings.IsHashtagStopPromptEnabled;
        _useLastHashtagDefault = _settings.UseLastHashtagAsDefault;
        _tableVersion = _settings.TableVersion;
        _dataDirectory = _dataDirectoryService.CurrentDirectory;
        _recordHint = string.Empty;

        ToggleRecordCommand = new RelayCommand(_ => ToggleRecording());
        SaveCommand = new RelayCommand(_ => Save());
        CloseCommand = new RelayCommand(_ => RequestClose?.Invoke(this, EventArgs.Empty));
        SelectDataDirectoryCommand = new RelayCommand(_ => SelectDataDirectory());
    }

    public event EventHandler? RequestClose;

    public string Hotkey
    {
        get => _hotkey;
        private set
        {
            if (_hotkey == value)
            {
                return;
            }

            _hotkey = value;
            NotifyPropertyChanged();
        }
    }

    public string GlobalTargetRateText
    {
        get => _globalTargetRateText;
        set
        {
            if (_globalTargetRateText == value)
            {
                return;
            }

            _globalTargetRateText = value;
            NotifyPropertyChanged();
        }
    }

    public bool HashtagStopPromptEnabled
    {
        get => _hashtagStopPromptEnabled;
        set
        {
            if (_hashtagStopPromptEnabled == value)
            {
                return;
            }

            _hashtagStopPromptEnabled = value;
            NotifyPropertyChanged();
        }
    }

    public bool UseLastHashtagDefault
    {
        get => _useLastHashtagDefault;
        set
        {
            if (_useLastHashtagDefault == value)
            {
                return;
            }

            _useLastHashtagDefault = value;
            NotifyPropertyChanged();
        }
    }

    public string TableVersion
    {
        get => _tableVersion;
        private set
        {
            if (_tableVersion == value)
            {
                return;
            }

            _tableVersion = value;
            NotifyPropertyChanged();
        }
    }

    public bool IsRecording
    {
        get => _isRecording;
        private set
        {
            if (_isRecording == value)
            {
                return;
            }

            _isRecording = value;
            NotifyPropertyChanged();
            NotifyPropertyChanged(nameof(RecordButtonText));
            RecordHint = _isRecording ? "Drücken Sie eine Tastenkombination (z.B. Ctrl+Alt+T)." : string.Empty;
        }
    }

    public string RecordHint
    {
        get => _recordHint;
        private set
        {
            if (_recordHint == value)
            {
                return;
            }

            _recordHint = value;
            NotifyPropertyChanged();
        }
    }

    public string RecordButtonText => IsRecording ? "Aufnahme stoppen" : "Hotkey aufnehmen";

    public string DataDirectory
    {
        get => _dataDirectory;
        private set
        {
            if (_dataDirectory == value)
            {
                return;
            }

            _dataDirectory = value;
            NotifyPropertyChanged();
        }
    }

    public ICommand ToggleRecordCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand CloseCommand { get; }

    public ICommand SelectDataDirectoryCommand { get; }

    public void CaptureHotkey(Key key, ModifierKeys modifiers)
    {
        if (!IsRecording)
        {
            return;
        }

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return;
        }

        if (modifiers == ModifierKeys.None)
        {
            RecordHint = "Bitte mindestens einen Modifier (Ctrl/Alt/Shift/Win) verwenden.";
            return;
        }

        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(key.ToString());
        Hotkey = string.Join("+", parts);
        IsRecording = false;
    }

    private void ToggleRecording()
    {
        IsRecording = !IsRecording;
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(Hotkey))
        {
            MessageBox.Show("Hotkey darf nicht leer sein.", "Einstellungen", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!decimal.TryParse(GlobalTargetRateText, NumberStyles.Number, CultureInfo.CurrentCulture, out var targetRate))
        {
            MessageBox.Show("Bitte einen gültigen Ziel-Stundensatz eingeben.", "Einstellungen", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var previousHotkey = _settings.Hotkey;
        if (!_hotkeyService.TryRegister(Hotkey, out var errorMessage))
        {
            _hotkeyService.TryRegister(previousHotkey, out _);
            MessageBox.Show(errorMessage ?? "Hotkey konnte nicht registriert werden.", "Einstellungen", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settings.SetHotkey(Hotkey);
        _settings.SetGlobalTargetRateEurPerHour(Math.Max(0m, targetRate));
        _settings.SetHashtagStopPromptEnabled(HashtagStopPromptEnabled);
        _settings.SetUseLastHashtagAsDefault(UseLastHashtagDefault);
        _settings.SetTableVersion(TableVersion);

        MessageBox.Show("Einstellungen gespeichert.", "Einstellungen", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SelectDataDirectory()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Datenordner auswählen",
            SelectedPath = _dataDirectoryService.CurrentDirectory,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        var selectedPath = dialog.SelectedPath;
        if (DataDirectoryService.AreSameDirectory(selectedPath, _dataDirectoryService.CurrentDirectory))
        {
            return;
        }

        if (!_dataDirectoryService.TryEnsureWritable(selectedPath, out var errorMessage))
        {
            MessageBox.Show(
                $"Der Ordner konnte nicht beschrieben werden:\n{errorMessage}",
                "Datenordner",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!ApplyDataDirectoryChange(selectedPath))
        {
            return;
        }

        DataDirectory = _dataDirectoryService.CurrentDirectory;
    }

    private bool ApplyDataDirectoryChange(string newDirectory)
    {
        var oldDirectory = _dataDirectoryService.CurrentDirectory;
        var oldDatabasePath = _dataDirectoryService.GetDatabasePath(oldDirectory);
        var newDatabasePath = _dataDirectoryService.GetDatabasePath(newDirectory);

        var shouldCopy = false;
        var shouldDeleteSource = false;

        if (File.Exists(oldDatabasePath))
        {
            if (File.Exists(newDatabasePath))
            {
                var result = MessageBox.Show(
                    "Im Zielordner existiert bereits eine Datenbank.\n\n" +
                    "Möchten Sie die vorhandene DB verwenden (Standard) oder überschreiben?",
                    "Datenordner",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxResult.Yes);

                if (result == MessageBoxResult.No)
                {
                    shouldCopy = true;
                    shouldDeleteSource = true;
                }
            }
            else
            {
                var result = MessageBox.Show(
                    "Vorhandene Daten in den neuen Ordner verschieben?",
                    "Datenordner",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxResult.Yes);

                if (result == MessageBoxResult.Yes)
                {
                    shouldCopy = true;
                    shouldDeleteSource = true;
                }
            }
        }

        if (shouldCopy)
        {
            if (!TryCopyDatabase(oldDatabasePath, newDatabasePath, out var copyError))
            {
                MessageBox.Show(
                    $"Migration fehlgeschlagen:\n{copyError}",
                    "Datenordner",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }

            if (shouldDeleteSource)
            {
                TryDeleteDatabase(oldDatabasePath);
            }
        }

        _dataDirectoryService.SetCurrentDirectory(newDirectory);
        _dataDirectoryService.PersistDirectory(newDirectory);
        _databaseService.Initialize();
        _settings.SetDataDirectory(newDirectory);
        LogService.UpdateLogDirectory(_dataDirectoryService.LogsDirectory);

        MessageBox.Show("Datenordner aktualisiert.", "Einstellungen", MessageBoxButton.OK, MessageBoxImage.Information);
        return true;
    }

    private static bool TryCopyDatabase(string sourcePath, string destinationPath, out string errorMessage)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? destinationPath);
            File.Copy(sourcePath, destinationPath, true);

            var info = new FileInfo(destinationPath);
            if (!info.Exists || info.Length <= 0)
            {
                errorMessage = "Die kopierte Datei ist leer.";
                return false;
            }

            using var connection = new SqliteConnection($"Data Source={destinationPath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA schema_version;";
            command.ExecuteScalar();

            errorMessage = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private static void TryDeleteDatabase(string databasePath)
    {
        try
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
        catch
        {
        }
    }
}
