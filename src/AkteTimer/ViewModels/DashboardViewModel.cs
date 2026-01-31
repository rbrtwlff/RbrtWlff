using System.Collections.ObjectModel;
using System.Globalization;
using AkteTimer.Models;
using AkteTimer.Services;

namespace AkteTimer.ViewModels;

public sealed class DashboardViewModel : ViewModelBase
{
    private readonly TimeEntryService _timeEntryService;
    private readonly DatabaseService _databaseService;
    private DateTime _fromDate;
    private DateTime _toDate;
    private int _totalTrackedMinutes;
    private int _totalBilledTrackedMinutes;
    private int _totalDummyMinutes;
    private decimal _totalBilledHourlyAmount;
    private decimal _totalBilledRvgAmount;
    private string _overallEffectiveRateText = "-";

    public DashboardViewModel(TimeEntryService timeEntryService, DatabaseService databaseService)
    {
        _timeEntryService = timeEntryService;
        _databaseService = databaseService;
        _fromDate = DateTime.Today.AddDays(-6);
        _toDate = DateTime.Today;
        Refresh();
    }

    public DateTime FromDate
    {
        get => _fromDate;
        set
        {
            if (_fromDate == value.Date)
            {
                return;
            }

            _fromDate = value.Date;
            NotifyPropertyChanged();
            if (_fromDate > ToDate)
            {
                ToDate = _fromDate;
            }

            Refresh();
        }
    }

    public DateTime ToDate
    {
        get => _toDate;
        set
        {
            if (_toDate == value.Date)
            {
                return;
            }

            _toDate = value.Date;
            NotifyPropertyChanged();
            if (_toDate < FromDate)
            {
                FromDate = _toDate;
            }

            Refresh();
        }
    }

    public int TotalTrackedMinutes
    {
        get => _totalTrackedMinutes;
        private set
        {
            if (_totalTrackedMinutes == value)
            {
                return;
            }

            _totalTrackedMinutes = value;
            NotifyPropertyChanged();
        }
    }

    public int TotalBilledTrackedMinutes
    {
        get => _totalBilledTrackedMinutes;
        private set
        {
            if (_totalBilledTrackedMinutes == value)
            {
                return;
            }

            _totalBilledTrackedMinutes = value;
            NotifyPropertyChanged();
        }
    }

    public int TotalDummyMinutes
    {
        get => _totalDummyMinutes;
        private set
        {
            if (_totalDummyMinutes == value)
            {
                return;
            }

            _totalDummyMinutes = value;
            NotifyPropertyChanged();
        }
    }

    public decimal TotalBilledHourlyAmount
    {
        get => _totalBilledHourlyAmount;
        private set
        {
            if (_totalBilledHourlyAmount == value)
            {
                return;
            }

            _totalBilledHourlyAmount = value;
            NotifyPropertyChanged();
        }
    }

    public decimal TotalBilledRvgAmount
    {
        get => _totalBilledRvgAmount;
        private set
        {
            if (_totalBilledRvgAmount == value)
            {
                return;
            }

            _totalBilledRvgAmount = value;
            NotifyPropertyChanged();
        }
    }

    public string OverallEffectiveRateText
    {
        get => _overallEffectiveRateText;
        private set
        {
            if (_overallEffectiveRateText == value)
            {
                return;
            }

            _overallEffectiveRateText = value;
            NotifyPropertyChanged();
        }
    }

    public ObservableCollection<RvgEfficiencyRowViewModel> TopRvgEfficiencyRows { get; } = new();

    public ObservableCollection<OpenTimeRowViewModel> OpenTimeRows { get; } = new();

    public void Refresh()
    {
        var matters = _timeEntryService.GetAllMatters();
        var matterLookup = matters.ToDictionary(matter => matter.Id);
        var matterIds = matterLookup.Keys.ToList();

        var entries = matterIds.Count == 0
            ? new List<TimeEntry>()
            : _timeEntryService.GetEntriesInRange(FromDate, ToDate, matterIds);

        var totalTrackedMinutes = 0;
        var totalBilledTrackedMinutes = 0;
        foreach (var entry in entries)
        {
            var roundedMinutes = GetRoundedMinutes(entry);
            totalTrackedMinutes += roundedMinutes;
            if (entry.Billed)
            {
                totalBilledTrackedMinutes += roundedMinutes;
            }
        }

        TotalTrackedMinutes = totalTrackedMinutes;
        TotalBilledTrackedMinutes = totalBilledTrackedMinutes;

        var (startUtc, endUtc) = GetUtcRange();
        TotalDummyMinutes = _databaseService.GetBillingAdjustmentMinutesDeltaSumInRange(startUtc, endUtc);
        TotalBilledHourlyAmount = _databaseService.GetBillingHourlyTotalAmountInRange(startUtc, endUtc);

        var snapshots = _databaseService.GetRvgBillingSnapshotsInRange(startUtc, endUtc);
        TotalBilledRvgAmount = snapshots.Sum(snapshot => snapshot.Total);

        var totalBilledAmount = TotalBilledHourlyAmount + TotalBilledRvgAmount;
        var totalBilledMinutes = TotalBilledTrackedMinutes + TotalDummyMinutes;
        OverallEffectiveRateText = totalBilledMinutes <= 0
            ? "-"
            : $"{(totalBilledAmount / (totalBilledMinutes / 60m)).ToString("N2", CultureInfo.CurrentCulture)} €/h";

        BuildRvgEfficiencyRows(entries, snapshots, matterLookup);
        BuildOpenTimeRows(entries, matterLookup);
    }

    private void BuildRvgEfficiencyRows(
        IReadOnlyCollection<TimeEntry> entries,
        IReadOnlyCollection<RvgBillingSnapshot> snapshots,
        IReadOnlyDictionary<long, Matter> matterLookup)
    {
        var rows = snapshots
            .GroupBy(snapshot => snapshot.MatterId)
            .Select(group =>
            {
                if (!matterLookup.TryGetValue(group.Key, out var matter))
                {
                    return null;
                }

                var rvgAmount = group.Sum(snapshot => snapshot.Total);
                // Work minutes use billed entries within the selected date range to reflect "Ist".
                var workMinutes = entries
                    .Where(entry => entry.MatterId == group.Key && entry.Billed)
                    .Sum(GetRoundedMinutes);
                var workHours = workMinutes / 60m;
                var targetRate = _timeEntryService.GetEffectiveTargetRate(matter);
                var hypotheticalTimeAmount = RvgCalculator.RoundCurrency(workHours * targetRate);
                var delta = RvgCalculator.RoundCurrency(rvgAmount - hypotheticalTimeAmount);
                var effectiveRate = workHours > 0m
                    ? RvgCalculator.RoundCurrency(rvgAmount / workHours)
                    : (decimal?)null;

                return new RvgEfficiencyRowViewModel(
                    matter.FileRef,
                    rvgAmount,
                    workHours,
                    hypotheticalTimeAmount,
                    delta,
                    effectiveRate);
            })
            .Where(row => row != null)
            .Cast<RvgEfficiencyRowViewModel>()
            .OrderByDescending(row => row.RvgEfficiencyDelta)
            .ThenByDescending(row => row.RvgAmount)
            .ToList();

        TopRvgEfficiencyRows.Clear();
        foreach (var row in rows)
        {
            TopRvgEfficiencyRows.Add(row);
        }
    }

    private void BuildOpenTimeRows(IReadOnlyCollection<TimeEntry> entries, IReadOnlyDictionary<long, Matter> matterLookup)
    {
        var rows = entries
            .Where(entry => !entry.Billed)
            .GroupBy(entry => entry.MatterId)
            .Select(group =>
            {
                var minutes = group.Sum(GetRoundedMinutes);
                if (minutes <= 0)
                {
                    return null;
                }

                var matterName = matterLookup.TryGetValue(group.Key, out var matter)
                    ? matter.FileRef
                    : "Unbekannt";
                return new OpenTimeRowViewModel(matterName, minutes);
            })
            .Where(row => row != null)
            .Cast<OpenTimeRowViewModel>()
            .OrderByDescending(row => row.UnbilledMinutes)
            .ThenBy(row => row.Matter)
            .ToList();

        OpenTimeRows.Clear();
        foreach (var row in rows)
        {
            OpenTimeRows.Add(row);
        }
    }

    private static int GetRoundedMinutes(TimeEntry entry)
    {
        var duration = TimeEntryCalculations.GetDuration(entry);
        var actualMinutes = TimeEntryCalculations.GetActualMinutes(duration);
        return TimeEntryCalculations.GetRoundedMinutes(actualMinutes);
    }

    private (DateTime startUtc, DateTime endUtc) GetUtcRange()
    {
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(FromDate.Date);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(ToDate.Date.AddDays(1));
        return (startUtc, endUtc);
    }
}

public sealed record RvgEfficiencyRowViewModel(
    string FileRef,
    decimal RvgAmount,
    decimal WorkHours,
    decimal HypotheticalTimeAmount,
    decimal RvgEfficiencyDelta,
    decimal? RvgEffectiveHourlyRate);

public sealed record OpenTimeRowViewModel(string Matter, int UnbilledMinutes);
