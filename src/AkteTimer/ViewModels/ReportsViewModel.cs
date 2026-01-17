using System.Collections.ObjectModel;
using AkteTimer.Models;
using AkteTimer.Services;
using System.Linq;

namespace AkteTimer.ViewModels;

public sealed class ReportsViewModel : ViewModelBase
{
    private readonly TimeEntryService _timeEntryService;
    private DateTime _fromDate;
    private DateTime _toDate;
    private bool _suppressMatterSelection;
    private string _todayTotalDuration = "00:00:00";
    private int _todayTotalMinutes;
    private int _todayTotalRoundedMinutes;
    private string _rangeTotalDuration = "00:00:00";
    private int _rangeTotalMinutes;
    private int _rangeTotalRoundedMinutes;
    private string _matterTotalDuration = "00:00:00";
    private int _matterTotalMinutes;
    private int _matterTotalRoundedMinutes;

    public ReportsViewModel(TimeEntryService timeEntryService)
    {
        _timeEntryService = timeEntryService;
        _fromDate = DateTime.Today.AddDays(-6);
        _toDate = DateTime.Today;

        foreach (var matter in _timeEntryService.GetAllMatters())
        {
            var item = new MatterFilterItem(matter) { IsSelected = true };
            item.SelectionChanged += HandleMatterSelectionChanged;
            MatterFilters.Add(item);
        }

        RefreshToday();
        RefreshRangeAndMatters();
    }

    public ObservableCollection<ReportEntryViewModel> TodayEntries { get; } = new();

    public ObservableCollection<MatterFilterItem> MatterFilters { get; } = new();

    public ObservableCollection<DayGroupViewModel> RangeGroups { get; } = new();

    public ObservableCollection<MatterGroupViewModel> MatterGroups { get; } = new();

    public DateTime FromDate
    {
        get => _fromDate;
        set
        {
            if (_fromDate == value)
            {
                return;
            }

            _fromDate = value.Date;
            NotifyPropertyChanged();
            if (_fromDate > ToDate)
            {
                ToDate = _fromDate;
            }

            RefreshRangeAndMatters();
        }
    }

    public DateTime ToDate
    {
        get => _toDate;
        set
        {
            if (_toDate == value)
            {
                return;
            }

            _toDate = value.Date;
            NotifyPropertyChanged();
            if (_toDate < FromDate)
            {
                FromDate = _toDate;
            }

            RefreshRangeAndMatters();
        }
    }

    public string TodayTotalDuration
    {
        get => _todayTotalDuration;
        private set
        {
            _todayTotalDuration = value;
            NotifyPropertyChanged();
        }
    }

    public int TodayTotalMinutes
    {
        get => _todayTotalMinutes;
        private set
        {
            _todayTotalMinutes = value;
            NotifyPropertyChanged();
        }
    }

    public int TodayTotalRoundedMinutes
    {
        get => _todayTotalRoundedMinutes;
        private set
        {
            _todayTotalRoundedMinutes = value;
            NotifyPropertyChanged();
        }
    }

    public string RangeTotalDuration
    {
        get => _rangeTotalDuration;
        private set
        {
            _rangeTotalDuration = value;
            NotifyPropertyChanged();
        }
    }

    public int RangeTotalMinutes
    {
        get => _rangeTotalMinutes;
        private set
        {
            _rangeTotalMinutes = value;
            NotifyPropertyChanged();
        }
    }

    public int RangeTotalRoundedMinutes
    {
        get => _rangeTotalRoundedMinutes;
        private set
        {
            _rangeTotalRoundedMinutes = value;
            NotifyPropertyChanged();
        }
    }

    public string MatterTotalDuration
    {
        get => _matterTotalDuration;
        private set
        {
            _matterTotalDuration = value;
            NotifyPropertyChanged();
        }
    }

    public int MatterTotalMinutes
    {
        get => _matterTotalMinutes;
        private set
        {
            _matterTotalMinutes = value;
            NotifyPropertyChanged();
        }
    }

    public int MatterTotalRoundedMinutes
    {
        get => _matterTotalRoundedMinutes;
        private set
        {
            _matterTotalRoundedMinutes = value;
            NotifyPropertyChanged();
        }
    }

    private void HandleMatterSelectionChanged(MatterFilterItem item)
    {
        if (_suppressMatterSelection)
        {
            return;
        }

        if (MatterFilters.All(filter => !filter.IsSelected))
        {
            _suppressMatterSelection = true;
            item.IsSelected = true;
            _suppressMatterSelection = false;
            return;
        }

        RefreshRangeAndMatters();
    }

    private void RefreshToday()
    {
        TodayEntries.Clear();
        var totalDuration = TimeSpan.Zero;
        var totalMinutes = 0;
        var totalRoundedMinutes = 0;
        foreach (var entry in _timeEntryService.GetTodayEntries())
        {
            var vm = new ReportEntryViewModel(entry);
            TodayEntries.Add(vm);
            totalDuration += vm.Duration;
            totalMinutes += vm.ActualMinutes;
            totalRoundedMinutes += vm.RoundedMinutes;
        }

        TodayTotalDuration = totalDuration.ToString(@"hh\:mm\:ss");
        TodayTotalMinutes = totalMinutes;
        TodayTotalRoundedMinutes = totalRoundedMinutes;
    }

    private void RefreshRangeAndMatters()
    {
        var selectedMatterIds = MatterFilters
            .Where(filter => filter.IsSelected)
            .Select(filter => filter.Matter.Id)
            .ToList();

        RangeGroups.Clear();
        MatterGroups.Clear();

        if (selectedMatterIds.Count == 0)
        {
            RangeTotalDuration = "00:00:00";
            RangeTotalMinutes = 0;
            RangeTotalRoundedMinutes = 0;
            MatterTotalDuration = "00:00:00";
            MatterTotalMinutes = 0;
            MatterTotalRoundedMinutes = 0;
            return;
        }

        var entries = _timeEntryService.GetEntriesInRange(FromDate, ToDate, selectedMatterIds);
        var entryViewModels = entries.Select(entry => new ReportEntryViewModel(entry)).ToList();

        var rangeGroups = entryViewModels
            .GroupBy(vm => vm.StartLocal.Date)
            .OrderBy(group => group.Key)
            .Select(group => new DayGroupViewModel(group.Key, group.OrderBy(vm => vm.StartLocal)));

        foreach (var group in rangeGroups)
        {
            RangeGroups.Add(group);
        }

        var matterGroups = entryViewModels
            .GroupBy(vm => vm.Matter)
            .OrderBy(group => group.Key)
            .Select(group => new MatterGroupViewModel(group.Key, group.OrderBy(vm => vm.StartLocal)));

        foreach (var group in matterGroups)
        {
            MatterGroups.Add(group);
        }

        var totalDuration = entryViewModels.Aggregate(TimeSpan.Zero, (current, vm) => current + vm.Duration);
        RangeTotalDuration = totalDuration.ToString(@"hh\:mm\:ss");
        RangeTotalMinutes = entryViewModels.Sum(vm => vm.ActualMinutes);
        RangeTotalRoundedMinutes = entryViewModels.Sum(vm => vm.RoundedMinutes);
        MatterTotalDuration = RangeTotalDuration;
        MatterTotalMinutes = RangeTotalMinutes;
        MatterTotalRoundedMinutes = RangeTotalRoundedMinutes;
    }
}

public sealed class MatterFilterItem : ViewModelBase
{
    private bool _isSelected;

    public MatterFilterItem(Matter matter)
    {
        Matter = matter;
    }

    public Matter Matter { get; }

    public string DisplayName => Matter.FileRef;

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
            SelectionChanged?.Invoke(this);
        }
    }

    public event Action<MatterFilterItem>? SelectionChanged;
}

public sealed class ReportEntryViewModel
{
    public ReportEntryViewModel(TimeEntry entry)
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

public sealed class DayGroupViewModel
{
    public DayGroupViewModel(DateTime date, IEnumerable<ReportEntryViewModel> entries)
    {
        Date = date;
        Entries = new ObservableCollection<ReportEntryViewModel>(entries);
        var totalDuration = Entries.Aggregate(TimeSpan.Zero, (current, vm) => current + vm.Duration);
        TotalDurationText = totalDuration.ToString(@"hh\:mm\:ss");
        TotalActualMinutes = Entries.Sum(vm => vm.ActualMinutes);
        TotalRoundedMinutes = Entries.Sum(vm => vm.RoundedMinutes);
    }

    public DateTime Date { get; }
    public string DateText => Date.ToString("dd.MM.yyyy");
    public ObservableCollection<ReportEntryViewModel> Entries { get; }
    public string TotalDurationText { get; }
    public int TotalActualMinutes { get; }
    public int TotalRoundedMinutes { get; }
}

public sealed class MatterGroupViewModel
{
    public MatterGroupViewModel(string matter, IEnumerable<ReportEntryViewModel> entries)
    {
        Matter = matter;
        Entries = new ObservableCollection<ReportEntryViewModel>(entries);
        var totalDuration = Entries.Aggregate(TimeSpan.Zero, (current, vm) => current + vm.Duration);
        TotalDurationText = totalDuration.ToString(@"hh\:mm\:ss");
        TotalActualMinutes = Entries.Sum(vm => vm.ActualMinutes);
        TotalRoundedMinutes = Entries.Sum(vm => vm.RoundedMinutes);
    }

    public string Matter { get; }
    public ObservableCollection<ReportEntryViewModel> Entries { get; }
    public string TotalDurationText { get; }
    public int TotalActualMinutes { get; }
    public int TotalRoundedMinutes { get; }
}
