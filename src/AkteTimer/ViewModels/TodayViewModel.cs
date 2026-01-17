using System.Collections.ObjectModel;
using AkteTimer.Models;
using AkteTimer.Services;

namespace AkteTimer.ViewModels;

public sealed class TodayViewModel : ViewModelBase
{
    private readonly TimeEntryService _timeEntryService;
    private string _totalDuration = "00:00:00";
    private int _totalActualMinutes;
    private int _totalRoundedMinutes;

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

    public int TotalActualMinutes
    {
        get => _totalActualMinutes;
        private set
        {
            _totalActualMinutes = value;
            NotifyPropertyChanged();
        }
    }

    public int TotalRoundedMinutes
    {
        get => _totalRoundedMinutes;
        private set
        {
            _totalRoundedMinutes = value;
            NotifyPropertyChanged();
        }
    }

    public void Refresh()
    {
        Entries.Clear();
        var entries = _timeEntryService.GetTodayEntries();
        var total = TimeSpan.Zero;
        var totalActualMinutes = 0;
        var totalRoundedMinutes = 0;
        foreach (var entry in entries)
        {
            var vm = new TodayEntryViewModel(entry);
            Entries.Add(vm);
            total += vm.Duration;
            totalActualMinutes += vm.ActualMinutes;
            totalRoundedMinutes += vm.RoundedMinutes;
        }

        TotalDuration = total.ToString(@"hh\:mm\:ss");
        TotalActualMinutes = totalActualMinutes;
        TotalRoundedMinutes = totalRoundedMinutes;
    }
}

public sealed class TodayEntryViewModel
{
    public TodayEntryViewModel(TimeEntry entry)
    {
        Matter = entry.MatterFileRef ?? "-";
        StartLocal = entry.StartUtc.ToLocalTime();
        EndLocal = (entry.EndUtc ?? DateTime.UtcNow).ToLocalTime();
        Duration = TimeEntryCalculations.GetDuration(entry);
        DurationText = Duration.ToString(@"hh\:mm\:ss");
        ActualMinutes = TimeEntryCalculations.GetActualMinutes(Duration);
        RoundedMinutes = TimeEntryCalculations.GetRoundedMinutes(ActualMinutes);
    }

    public string Matter { get; }
    public DateTime StartLocal { get; }
    public DateTime EndLocal { get; }
    public TimeSpan Duration { get; }
    public string DurationText { get; }
    public int ActualMinutes { get; }
    public int RoundedMinutes { get; }
}
