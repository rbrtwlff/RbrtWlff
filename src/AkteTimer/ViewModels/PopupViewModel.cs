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
    private RecentEntryDisplay? _selectedRecentEntry;
    private bool _isHotkeyHelpVisible;
    private bool _shouldFocusFileRefInput;
    private string _stateText = "Idle";
    private bool _isRunning;
    private bool _isPaused;
    private bool _isIdle = true;
    private bool _hasActiveMatter;
    private string _fileRefValidationMessage = string.Empty;
    private string _selectedHashtag = string.Empty;
    private TimeEntry? _lastStoppedEntryForNotePrompt;

    public PopupViewModel(TimeEntryService timeEntryService, SettingsService settingsService)
    {
        _timeEntryService = timeEntryService;
        _settingsService = settingsService;
        _timeEntryService.StateChanged += (_, _) => Refresh();

        ToggleCommand = new RelayCommand(_ => Toggle());
        SelectHashtagCommand = new RelayCommand(option => SelectHashtag(option as HashtagOption));

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += (_, _) => RefreshDurations();
        _timer.Start();

        _isHotkeyHelpVisible = _settingsService.IsPopupHotkeyHelpVisible;
        InitializeHashtags();
        _selectedHashtag = _settingsService.LastHashtag;
        LoadRecentMatters();
        Refresh();
    }

    public ObservableCollection<RecentEntryDisplay> RecentEntries { get; } = new();

    public ObservableCollection<HashtagOption> HashtagOptions { get; } = new();

    public RelayCommand ToggleCommand { get; }

    public RelayCommand SelectHashtagCommand { get; }

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

            if (_selectedRecentEntry != null && _selectedRecentEntry.MatterFileRef != value)
            {
                _selectedRecentEntry = null;
                NotifyPropertyChanged(nameof(SelectedRecentEntry));
            }

            UpdateFileRefValidation();
        }
    }

    public RecentEntryDisplay? SelectedRecentEntry
    {
        get => _selectedRecentEntry;
        set
        {
            if (_selectedRecentEntry == value)
            {
                return;
            }

            _selectedRecentEntry = value;
            NotifyPropertyChanged();
            if (!string.IsNullOrWhiteSpace(value?.MatterFileRef) && FileRefInput != value.MatterFileRef)
            {
                FileRefInput = value.MatterFileRef;
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
        set
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

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (_isRunning == value)
            {
                return;
            }

            _isRunning = value;
            NotifyPropertyChanged();
        }
    }

    public bool IsPaused
    {
        get => _isPaused;
        private set
        {
            if (_isPaused == value)
            {
                return;
            }

            _isPaused = value;
            NotifyPropertyChanged();
        }
    }

    public bool IsIdle
    {
        get => _isIdle;
        private set
        {
            if (_isIdle == value)
            {
                return;
            }

            _isIdle = value;
            NotifyPropertyChanged();
        }
    }

    public bool HasActiveMatter
    {
        get => _hasActiveMatter;
        private set
        {
            if (_hasActiveMatter == value)
            {
                return;
            }

            _hasActiveMatter = value;
            NotifyPropertyChanged();
        }
    }

    public bool CanStart => IsPaused && HasActiveMatter && _timeEntryService.IsActiveMatterConfirmed;

    public bool CanPause => IsRunning;

    public bool CanStop => !IsIdle;

    public void PauseAndTrackLastStoppedEntry()
    {
        _lastStoppedEntryForNotePrompt = _timeEntryService.PauseAndReturnStoppedEntry();
    }

    public TimeEntry? ConsumeLastStoppedEntryForNotePrompt()
    {
        var entry = _lastStoppedEntryForNotePrompt;
        _lastStoppedEntryForNotePrompt = null;
        return entry;
    }

    public void ClearLastStoppedEntryForNotePrompt()
    {
        _lastStoppedEntryForNotePrompt = null;
    }

    public string FileRefValidationMessage
    {
        get => _fileRefValidationMessage;
        private set
        {
            if (_fileRefValidationMessage == value)
            {
                return;
            }

            _fileRefValidationMessage = value;
            NotifyPropertyChanged();
            NotifyPropertyChanged(nameof(IsFileRefInvalid));
        }
    }

    public bool IsFileRefInvalid => !string.IsNullOrWhiteSpace(FileRefValidationMessage);

    public string SelectedHashtag
    {
        get => _selectedHashtag;
        private set
        {
            if (_selectedHashtag == value)
            {
                return;
            }

            _selectedHashtag = value;
            NotifyPropertyChanged();
        }
    }

    public void MoveSelection(int delta)
    {
        if (RecentEntries.Count == 0)
        {
            return;
        }

        var index = SelectedRecentEntry == null ? -1 : RecentEntries.IndexOf(SelectedRecentEntry);
        index = index < 0 ? 0 : index + delta;
        index = Math.Clamp(index, 0, RecentEntries.Count - 1);
        SelectedRecentEntry = RecentEntries[index];
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
            IsRunning = false;
            IsPaused = false;
            IsIdle = true;
            HasActiveMatter = false;
            focusNow = !ShouldFocusFileRefInput;
            ShouldFocusFileRefInput = true;
        }
        else if (running)
        {
            StateText = "Running";
            StatusText = "Läuft";
            ActiveMatterText = _timeEntryService.ActiveMatterFileRef ?? "Keine aktive Akte";
            ToggleButtonText = "Pause";
            IsRunning = true;
            IsPaused = false;
            IsIdle = false;
            HasActiveMatter = true;
            ShouldFocusFileRefInput = false;
        }
        else
        {
            StateText = "Paused";
            StatusText = "Pausiert";
            ActiveMatterText = _timeEntryService.ActiveMatterFileRef ?? "Keine aktive Akte";
            ToggleButtonText = "Start";
            IsRunning = false;
            IsPaused = true;
            IsIdle = false;
            HasActiveMatter = true;
            ShouldFocusFileRefInput = false;
        }

        NotifyPropertyChanged(nameof(CanStart));
        NotifyPropertyChanged(nameof(CanPause));
        NotifyPropertyChanged(nameof(CanStop));

        RefreshDurations();
        LoadRecentMatters();

        if (focusNow)
        {
            FocusRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Toggle()
    {
        if (_timeEntryService.IsRunning)
        {
            PauseAndTrackLastStoppedEntry();
            return;
        }

        ClearLastStoppedEntryForNotePrompt();
        _timeEntryService.Start();
    }

    private void LoadRecentMatters()
    {
        var previousMatter = SelectedRecentEntry?.MatterFileRef;
        RecentEntries.Clear();
        foreach (var entry in GetRecentEntries())
        {
            RecentEntries.Add(entry);
        }

        if (!string.IsNullOrWhiteSpace(previousMatter))
        {
            SelectedRecentEntry = RecentEntries.FirstOrDefault(entry =>
                string.Equals(entry.MatterFileRef, previousMatter, StringComparison.OrdinalIgnoreCase));
            return;
        }

        SelectedRecentEntry = null;
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

    private void InitializeHashtags()
    {
        HashtagOptions.Clear();
        var index = 1;
        foreach (var hashtag in TimeEntryService.DefaultHashtags)
        {
            var hotkey = hashtag == "#Sonstiges" ? "Ctrl+0" : $"Ctrl+{index}";
            HashtagOptions.Add(new HashtagOption(hashtag, hotkey));
            index++;
        }

        UpdateSelectedHashtag(_settingsService.LastHashtag);
    }

    private void SelectHashtag(HashtagOption? option)
    {
        if (option == null)
        {
            return;
        }

        var runningEntry = _timeEntryService.GetRunningEntry();
        if (runningEntry != null)
        {
            _timeEntryService.SetEntryHashtag(runningEntry.Id, option.Text);
        }
        else
        {
            _settingsService.SetLastHashtag(option.Text);
        }

        UpdateSelectedHashtag(option.Text);
    }

    private void UpdateSelectedHashtag(string hashtag)
    {
        SelectedHashtag = hashtag;
        foreach (var option in HashtagOptions)
        {
            option.IsSelected = string.Equals(option.Text, hashtag, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void UpdateFileRefValidation()
    {
        if (string.IsNullOrWhiteSpace(FileRefInput))
        {
            FileRefValidationMessage = string.Empty;
            return;
        }

        FileRefValidationMessage = _timeEntryService.IsValidFileRef(FileRefInput)
            ? string.Empty
            : "Format: 1234/25";
    }

    private IEnumerable<RecentEntryDisplay> GetRecentEntries()
    {
        var entries = _timeEntryService.GetTodayEntries()
            .OrderByDescending(entry => entry.StartUtc)
            .Take(5);

        foreach (var entry in entries)
        {
            yield return new RecentEntryDisplay(
                entry.MatterFileRef ?? "Unbekannt",
                entry.Hashtag ?? "Kein Hashtag",
                FormatEntryTimestamp(entry.StartUtc),
                FormatDuration(GetEntryDuration(entry)));
        }
    }

    private static string FormatEntryTimestamp(DateTime startUtc)
    {
        var local = startUtc.ToLocalTime();
        var label = local.Date == DateTime.Now.Date ? "Heute" : local.ToString("dd.MM");
        return $"{label} {local:HH:mm}";
    }

    private static TimeSpan GetEntryDuration(TimeEntry entry)
    {
        var end = entry.EndUtc ?? DateTime.UtcNow;
        return end - entry.StartUtc;
    }
}

public sealed class RecentEntryDisplay
{
    public RecentEntryDisplay(string matterFileRef, string hashtag, string startedText, string durationText)
    {
        MatterFileRef = matterFileRef;
        Hashtag = hashtag;
        StartedText = startedText;
        DurationText = durationText;
    }

    public string MatterFileRef { get; }
    public string Hashtag { get; }
    public string StartedText { get; }
    public string DurationText { get; }
}

public sealed class HashtagOption : ViewModelBase
{
    private bool _isSelected;

    public HashtagOption(string text, string hotkey)
    {
        Text = text;
        Hotkey = hotkey;
    }

    public string Text { get; }

    public string Hotkey { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            NotifyPropertyChanged();
        }
    }
}
