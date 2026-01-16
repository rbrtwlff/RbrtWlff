using System.Collections.ObjectModel;
using System.Windows.Threading;
using AkteTimer.Models;
using AkteTimer.Services;

namespace AkteTimer.ViewModels;

public sealed class PopupViewModel : ViewModelBase
{
    private readonly TimeEntryService _timeEntryService;
    private readonly DispatcherTimer _timer;
    private string _fileRefInput = string.Empty;
    private string _statusText = "Pausiert";
    private string _activeMatterText = "-";
    private string _todayDurationText = "00:00:00";
    private string _entryDurationText = "00:00:00";

    public PopupViewModel(TimeEntryService timeEntryService)
    {
        _timeEntryService = timeEntryService;
        _timeEntryService.StateChanged += (_, _) => Refresh();

        ToggleCommand = new RelayCommand(_ => Toggle());

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += (_, _) => RefreshDurations();
        _timer.Start();

        LoadRecentMatters();
        Refresh();
    }

    public ObservableCollection<string> RecentMatters { get; } = new();

    public RelayCommand ToggleCommand { get; }

    public string FileRefInput
    {
        get => _fileRefInput;
        set
        {
            _fileRefInput = value;
            NotifyPropertyChanged();
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

    public void MoveSelection(int delta)
    {
        if (RecentMatters.Count == 0)
        {
            return;
        }

        var index = RecentMatters.IndexOf(FileRefInput);
        index = index < 0 ? 0 : index + delta;
        index = Math.Clamp(index, 0, RecentMatters.Count - 1);
        FileRefInput = RecentMatters[index];
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
        StatusText = _timeEntryService.IsRunning ? "Läuft" : "Pausiert";
        ActiveMatterText = _timeEntryService.ActiveMatterFileRef ?? "-";
        RefreshDurations();
        LoadRecentMatters();
    }

    private void Toggle()
    {
        _timeEntryService.ToggleStartPause();
    }

    private void LoadRecentMatters()
    {
        RecentMatters.Clear();
        foreach (var matter in _timeEntryService.GetRecentMatters())
        {
            RecentMatters.Add(matter.FileRef);
        }
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
