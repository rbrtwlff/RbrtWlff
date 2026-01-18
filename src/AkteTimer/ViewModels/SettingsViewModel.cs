using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using AkteTimer.Services;

namespace AkteTimer.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settings;
    private readonly HotkeyService _hotkeyService;
    private string _hotkey;
    private string _globalTargetRateText;
    private bool _hashtagStopPromptEnabled;
    private bool _useLastHashtagDefault;
    private string _tableVersion;
    private bool _isRecording;
    private string _recordHint;

    public SettingsViewModel(SettingsService settings, HotkeyService hotkeyService)
    {
        _settings = settings;
        _hotkeyService = hotkeyService;
        _hotkey = _settings.Hotkey;
        _globalTargetRateText = _settings.GlobalTargetRateEurPerHour.ToString("N2", CultureInfo.CurrentCulture);
        _hashtagStopPromptEnabled = _settings.IsHashtagStopPromptEnabled;
        _useLastHashtagDefault = _settings.UseLastHashtagAsDefault;
        _tableVersion = _settings.TableVersion;
        _recordHint = string.Empty;

        ToggleRecordCommand = new RelayCommand(_ => ToggleRecording());
        SaveCommand = new RelayCommand(_ => Save());
        CloseCommand = new RelayCommand(_ => RequestClose?.Invoke(this, EventArgs.Empty));
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

    public ICommand ToggleRecordCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand CloseCommand { get; }

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
}
