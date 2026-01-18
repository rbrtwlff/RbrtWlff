using System.Collections.ObjectModel;
using System.Windows.Threading;
using AkteTimer.Models;
using AkteTimer.Services;

namespace AkteTimer.ViewModels;

public sealed class PopupViewModel : ViewModelBase
{
    private readonly TimeEntryService _timeEntryService;
    private readonly SettingsService _settingsService;
    private readonly DispatcherTimer _timer;
    private string _fileRefInput = string.Empty;
    private string _statusText = "Pausiert";
    private string _activeMatterText = "Keine aktive Akte";
    private string _todayDurationText = "00:00:00";
    private string _entryDurationText = "00:00:00";
    private string _toggleButtonText = "Start";
    private string? _selectedRecentMatter;
    private bool _isHotkeyHelpVisible;
    private bool _shouldFocusFileRefInput;
    private string _stateText = "Idle";

    public PopupViewModel(TimeEntryService timeEntryService, SettingsService settingsService)
    {
        _timeEntryService = timeEntryService;
        _settingsService = settingsService;
        _timeEntryService.StateChanged += (_, _) => Refresh();

        ToggleCommand = new RelayCommand(_ => Toggle());
        ToggleHotkeyHelpCommand = new RelayCommand(_ => ToggleHotkeyHelp());

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += (_, _) => RefreshDurations();
        _timer.Start();

        _isHotkeyHelpVisible = _settingsService.IsPopupHotkeyHelpVisible;
        LoadRecentMatters();
        Refresh();
    }

    public ObservableCollection<string> RecentMatters { get; } = new();

    public RelayCommand ToggleCommand { get; }

    public RelayCommand ToggleHotkeyHelpCommand { get; }

    public event EventHandler? FocusRequested;

    public string StateText
    {
        get => _stateText;
        private set
        {
            _stateText = value;
            NotifyPropertyChanged();
        }
    }

    public string FileRefInput
    {
        get => _fileRefInput;
        set
        {
            if (_fileRefInput == value)
            {
                return;
            }

            _fileRefInput = value;
            NotifyPropertyChanged();

            if (_selectedRecentMatter != null && _selectedRecentMatter != value)
            {
                _selectedRecentMatter = null;
                NotifyPropertyChanged(nameof(SelectedRecentMatter));
            }
        }
    }

    public string? SelectedRecentMatter
    {
        get => _selectedRecentMatter;
        set
        {
            if (_selectedRecentMatter == value)
            {
                return;
            }

            _selectedRecentMatter = value;
            NotifyPropertyChanged();
            if (!string.IsNullOrWhiteSpace(value) && FileRefInput != value)
            {
                FileRefInput = value;
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            _statusText = value;
            NotifyPropertyChanged();
        }
    }

    public string ActiveMatterText
    {
        get => _activeMatterText;
        private set
        {
            _activeMatterText = value;
            NotifyPropertyChanged();
        }
    }

    public string TodayDurationText
    {
        get => _todayDurationText;
        private set
        {
            _todayDurationText = value;
            NotifyPropertyChanged();
        }
    }

    public string EntryDurationText
    {
        get => _entryDurationText;
        private set
        {
            _entryDurationText = value;
            NotifyPropertyChanged();
        }
    }

    public string ToggleButtonText
    {
        get => _toggleButtonText;
        private set
        {
            _toggleButtonText = value;
            NotifyPropertyChanged();
        }
    }

    public bool ShouldFocusFileRefInput
    {
        get => _shouldFocusFileRefInput;
        private set
        {
            if (_shouldFocusFileRefInput == value)
            {
                return;
            }

            _shouldFocusFileRefInput = value;
            NotifyPropertyChanged();
        }
    }

    public bool IsHotkeyHelpVisible
    {
        get => _isHotkeyHelpVisible;
        private set
        {
            if (_isHotkeyHelpVisible == value)
            {
                return;
            }

            _isHotkeyHelpVisible = value;
            _settingsService.SetPopupHotkeyHelpVisible(value);
            NotifyPropertyChanged();
            NotifyPropertyChanged(nameof(HotkeyHelpToggleText));
        }
    }

    public string HotkeyHelpToggleText => IsHotkeyHelpVisible ? "Hotkey-Hilfe ausblenden" : "Hotkey-Hilfe anzeigen";

    public void MoveSelection(int delta)
    {
        if (RecentMatters.Count == 0)
        {
            return;
        }

        var index = SelectedRecentMatter == null ? -1 : RecentMatters.IndexOf(SelectedRecentMatter);
        index = index < 0 ? 0 : index + delta;
        index = Math.Clamp(index, 0, RecentMatters.Count - 1);
        SelectedRecentMatter = RecentMatters[index];
    }

    public void UpdateStatus(string message)
    {
        StatusText = message;
    }

    public void ClearInput()
    {
        FileRefInput = string.Empty;
    }

    public void RefreshRecent()
    {
        LoadRecentMatters();
    }

    public void Refresh()
    {
        var running = _timeEntryService.IsRunning;
        var hasMatter = !string.IsNullOrWhiteSpace(_timeEntryService.ActiveMatterFileRef);
        var focusNow = false;

        if (!hasMatter)
        {
            StateText = "Idle";
            StatusText = "Pausiert";
            ActiveMatterText = "Keine aktive Akte";
            ToggleButtonText = "Start";
            focusNow = !ShouldFocusFileRefInput;
            ShouldFocusFileRefInput = true;
        }
        else if (running)
        {
            StateText = "Running";
            StatusText = "Läuft";
            ActiveMatterText = _timeEntryService.ActiveMatterFileRef ?? "Keine aktive Akte";
            ToggleButtonText = "Pause";
            ShouldFocusFileRefInput = false;
        }
        else
        {
            StateText = "Paused";
            StatusText = "Pausiert";
            ActiveMatterText = _timeEntryService.ActiveMatterFileRef ?? "Keine aktive Akte";
            ToggleButtonText = "Start";
            ShouldFocusFileRefInput = false;
        }

        RefreshDurations();
        LoadRecentMatters();

        if (focusNow)
        {
            FocusRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Toggle()
    {
        _timeEntryService.ToggleStartPause();
    }

    private void ToggleHotkeyHelp()
    {
        IsHotkeyHelpVisible = !IsHotkeyHelpVisible;
    }

    private void LoadRecentMatters()
    {
        var previousSelection = SelectedRecentMatter;
        RecentMatters.Clear();
        foreach (var matter in _timeEntryService.GetRecentMatters())
        {
            RecentMatters.Add(matter.FileRef);
        }

        if (previousSelection != null && RecentMatters.Contains(previousSelection))
        {
            SelectedRecentMatter = previousSelection;
            return;
        }

        SelectedRecentMatter = null;
    }

    private void RefreshDurations()
    {
        var entries = _timeEntryService.GetTodayEntries();
        var running = _timeEntryService.GetRunningEntry();
        var todayTotal = CalculateDuration(entries);
        TodayDurationText = FormatDuration(todayTotal);

        if (running == null)
        {
            EntryDurationText = "00:00:00";
            return;
        }

        var entryDuration = DateTime.UtcNow - running.StartUtc;
        EntryDurationText = FormatDuration(entryDuration);
    }

    private static TimeSpan CalculateDuration(IEnumerable<TimeEntry> entries)
    {
        var total = TimeSpan.Zero;
        foreach (var entry in entries)
        {
            var end = entry.EndUtc ?? DateTime.UtcNow;
            total += end - entry.StartUtc;
        }
        return total;
    }

    private static string FormatDuration(TimeSpan span)
    {
        return span.ToString(@"hh\:mm\:ss");
    }
}
