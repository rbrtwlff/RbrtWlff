using System.Collections.ObjectModel;
using AkteTimer.Models;
using AkteTimer.Services;

namespace AkteTimer.ViewModels;

public sealed class TodayViewModel : ViewModelBase
{
    private readonly TimeEntryService _timeEntryService;
    private string _totalDuration = "00:00:00";

    public TodayViewModel(TimeEntryService timeEntryService)
    {
        _timeEntryService = timeEntryService;
        Refresh();
    }

    public ObservableCollection<TodayEntryViewModel> Entries { get; } = new();

    public string TotalDuration
    {
        get => _totalDuration;
        private set
        {
            _totalDuration = value;
            NotifyPropertyChanged();
        }
    }

    public void Refresh()
    {
        Entries.Clear();
        var entries = _timeEntryService.GetTodayEntries();
        var total = TimeSpan.Zero;
        foreach (var entry in entries)
        {
            var vm = new TodayEntryViewModel(entry);
            Entries.Add(vm);
            total += vm.Duration;
        }

        TotalDuration = total.ToString(@"hh\:mm\:ss");
    }
}

public sealed class TodayEntryViewModel
{
    public TodayEntryViewModel(TimeEntry entry)
    {
        Matter = entry.MatterFileRef ?? "-";
        var end = entry.EndUtc ?? DateTime.UtcNow;
        StartLocal = entry.StartUtc.ToLocalTime();
        EndLocal = end.ToLocalTime();
        Duration = end - entry.StartUtc;
        DurationText = Duration.ToString(@"hh\:mm\:ss");
    }

    public string Matter { get; }
    public DateTime StartLocal { get; }
    public DateTime EndLocal { get; }
    public TimeSpan Duration { get; }
    public string DurationText { get; }
}
