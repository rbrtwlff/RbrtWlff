using System.Collections.ObjectModel;
using AkteTimer.Models;
using AkteTimer.Services;

namespace AkteTimer.ViewModels;

public sealed class DashboardViewModel : ViewModelBase
{
    private readonly TimeEntryService _timeEntryService;
    private readonly DatabaseService _databaseService;
    private DateTime _fromDate;
    private DateTime _toDate;
    private DashboardDateMode _dateMode = DashboardDateMode.WorkingDate;
    private int _totalTrackedMinutes;
    private int _hourlyTrackedMinutes;
    private int _rvgTrackedMinutes;
    private int _hourlyDummyMinutes;
    private decimal _hourlyTrackedAmountTrackedOnly;
    private decimal _hourlyTrackedAmountWithDummy;
    private decimal _rvgHypotheticalTimeAmount;
    private decimal _rvgBilledAmount;
    private decimal _rvgEfficiencyDelta;
    private decimal? _rvgEffectiveHourlyRate;
    private decimal? _overallEffectiveHourlyRate;

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

    public bool IsWorkingDateMode
    {
        get => _dateMode == DashboardDateMode.WorkingDate;
        set
        {
            if (!value || _dateMode == DashboardDateMode.WorkingDate)
            {
                return;
            }

            _dateMode = DashboardDateMode.WorkingDate;
            NotifyPropertyChanged();
            NotifyPropertyChanged(nameof(IsBillingDateMode));
            Refresh();
        }
    }

    public bool IsBillingDateMode
    {
        get => _dateMode == DashboardDateMode.BillingDate;
        set
        {
            if (!value || _dateMode == DashboardDateMode.BillingDate)
            {
                return;
            }

            _dateMode = DashboardDateMode.BillingDate;
            NotifyPropertyChanged();
            NotifyPropertyChanged(nameof(IsWorkingDateMode));
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

    public int HourlyTrackedMinutes
    {
        get => _hourlyTrackedMinutes;
        private set
        {
            if (_hourlyTrackedMinutes == value)
            {
                return;
            }

            _hourlyTrackedMinutes = value;
            NotifyPropertyChanged();
        }
    }

    public int RvgTrackedMinutes
    {
        get => _rvgTrackedMinutes;
        private set
        {
            if (_rvgTrackedMinutes == value)
            {
                return;
            }

            _rvgTrackedMinutes = value;
            NotifyPropertyChanged();
        }
    }

    public int HourlyDummyMinutes
    {
        get => _hourlyDummyMinutes;
        private set
        {
            if (_hourlyDummyMinutes == value)
            {
                return;
            }

            _hourlyDummyMinutes = value;
            NotifyPropertyChanged();
        }
    }

    public decimal HourlyTrackedAmountTrackedOnly
    {
        get => _hourlyTrackedAmountTrackedOnly;
        private set
        {
            if (_hourlyTrackedAmountTrackedOnly == value)
            {
                return;
            }

            _hourlyTrackedAmountTrackedOnly = value;
            NotifyPropertyChanged();
        }
    }

    public decimal HourlyTrackedAmountWithDummy
    {
        get => _hourlyTrackedAmountWithDummy;
        private set
        {
            if (_hourlyTrackedAmountWithDummy == value)
            {
                return;
            }

            _hourlyTrackedAmountWithDummy = value;
            NotifyPropertyChanged();
        }
    }

    public decimal RvgHypotheticalTimeAmount
    {
        get => _rvgHypotheticalTimeAmount;
        private set
        {
            if (_rvgHypotheticalTimeAmount == value)
            {
                return;
            }

            _rvgHypotheticalTimeAmount = value;
            NotifyPropertyChanged();
        }
    }

    public decimal RvgBilledAmount
    {
        get => _rvgBilledAmount;
        private set
        {
            if (_rvgBilledAmount == value)
            {
                return;
            }

            _rvgBilledAmount = value;
            NotifyPropertyChanged();
        }
    }

    public decimal RvgEfficiencyDelta
    {
        get => _rvgEfficiencyDelta;
        private set
        {
            if (_rvgEfficiencyDelta == value)
            {
                return;
            }

            _rvgEfficiencyDelta = value;
            NotifyPropertyChanged();
        }
    }

    public decimal? RvgEffectiveHourlyRate
    {
        get => _rvgEffectiveHourlyRate;
        private set
        {
            if (_rvgEffectiveHourlyRate == value)
            {
                return;
            }

            _rvgEffectiveHourlyRate = value;
            NotifyPropertyChanged();
        }
    }

    public decimal? OverallEffectiveHourlyRate
    {
        get => _overallEffectiveHourlyRate;
        private set
        {
            if (_overallEffectiveHourlyRate == value)
            {
                return;
            }

            _overallEffectiveHourlyRate = value;
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

        var (startUtc, endUtc) = GetUtcRange();
        IReadOnlyCollection<TimeEntry> entries;
        IReadOnlyCollection<RvgBillingSnapshot> snapshots;
        int hourlyDummyMinutes;
        decimal hourlyDummyAmount = 0m;
        decimal hourlyTrackedAmount = 0m;

        if (_dateMode == DashboardDateMode.BillingDate)
        {
            var batches = _databaseService.GetBillingBatchesInRange(startUtc, endUtc);
            var batchIds = batches.Select(batch => batch.Id).ToList();
            entries = batchIds.Count == 0
                ? new List<TimeEntry>()
                : _databaseService.GetEntriesForBillingBatches(batchIds);

            var billingCases = batchIds.Count == 0
                ? new List<BillingCase>()
                : _databaseService.GetBillingCasesForBatches(batchIds);
            var hourlyCases = billingCases.Where(billingCase => billingCase.BillingType == BillingType.Hourly).ToList();

            hourlyDummyMinutes = hourlyCases.Sum(billingCase => billingCase.DummyMinutes);
            hourlyTrackedAmount = hourlyCases.Sum(billingCase => billingCase.TrackedAmount);
            hourlyDummyAmount = hourlyCases.Sum(billingCase => billingCase.TotalAmount - billingCase.TrackedAmount);
            HourlyTrackedAmountWithDummy = RvgCalculator.RoundCurrency(hourlyCases.Sum(billingCase => billingCase.TotalAmount));
        }
        else
        {
            entries = matterIds.Count == 0
                ? new List<TimeEntry>()
                : _timeEntryService.GetEntriesInRange(FromDate, ToDate, matterIds);

            var adjustments = _databaseService.GetHourlyBillingAdjustmentsInRange(startUtc, endUtc);
            hourlyDummyMinutes = adjustments.Sum(adjustment => adjustment.MinutesDelta);
            hourlyDummyAmount = 0m;
            foreach (var adjustment in adjustments)
            {
                if (matterLookup.TryGetValue(adjustment.MatterId, out var matter))
                {
                    hourlyDummyAmount += (adjustment.MinutesDelta / 60m) * matter.HourlyRateEurPerHour;
                }
            }
        }

        var totalTrackedMinutes = 0;
        var hourlyTrackedMinutes = 0;
        var rvgTrackedMinutes = 0;
        var hourlyTrackedAmountTrackedOnly = 0m;
        var rvgHypotheticalTimeAmount = 0m;
        foreach (var entry in entries)
        {
            if (!matterLookup.TryGetValue(entry.MatterId, out var matter))
            {
                continue;
            }

            var roundedMinutes = GetRoundedMinutes(entry);
            totalTrackedMinutes += roundedMinutes;

            if (matter.BillingType == BillingType.Rvg)
            {
                rvgTrackedMinutes += roundedMinutes;
                var targetRate = _timeEntryService.GetEffectiveTargetRate(matter);
                rvgHypotheticalTimeAmount += (roundedMinutes / 60m) * targetRate;
            }
            else
            {
                hourlyTrackedMinutes += roundedMinutes;
                hourlyTrackedAmountTrackedOnly += (roundedMinutes / 60m) * matter.HourlyRateEurPerHour;
            }
        }

        TotalTrackedMinutes = totalTrackedMinutes;
        HourlyTrackedMinutes = hourlyTrackedMinutes;
        RvgTrackedMinutes = rvgTrackedMinutes;
        HourlyDummyMinutes = hourlyDummyMinutes;
        HourlyTrackedAmountTrackedOnly = RvgCalculator.RoundCurrency(_dateMode == DashboardDateMode.BillingDate
            ? hourlyTrackedAmount
            : hourlyTrackedAmountTrackedOnly);
        if (_dateMode != DashboardDateMode.BillingDate)
        {
            HourlyTrackedAmountWithDummy = RvgCalculator.RoundCurrency(hourlyTrackedAmountTrackedOnly + hourlyDummyAmount);
        }

        RvgHypotheticalTimeAmount = RvgCalculator.RoundCurrency(rvgHypotheticalTimeAmount);

        snapshots = _databaseService.GetRvgBillingSnapshotsInRange(startUtc, endUtc);
        RvgBilledAmount = RvgCalculator.RoundCurrency(snapshots.Sum(snapshot => snapshot.Total));
        RvgEfficiencyDelta = RvgCalculator.RoundCurrency(RvgBilledAmount - RvgHypotheticalTimeAmount);
        RvgEffectiveHourlyRate = rvgTrackedMinutes > 0
            ? RvgCalculator.RoundCurrency(RvgBilledAmount / (rvgTrackedMinutes / 60m))
            : null;

        var totalMinutesForRate = totalTrackedMinutes + hourlyDummyMinutes;
        OverallEffectiveHourlyRate = totalMinutesForRate <= 0
            ? null
            : RvgCalculator.RoundCurrency((HourlyTrackedAmountWithDummy + RvgBilledAmount) / (totalMinutesForRate / 60m));

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
                var workMinutes = entries
                    .Where(entry => entry.MatterId == group.Key)
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

public enum DashboardDateMode
{
    WorkingDate,
    BillingDate
}

public sealed record RvgEfficiencyRowViewModel(
    string FileRef,
    decimal RvgAmount,
    decimal WorkHours,
    decimal HypotheticalTimeAmount,
    decimal RvgEfficiencyDelta,
    decimal? RvgEffectiveHourlyRate);

public sealed record OpenTimeRowViewModel(string Matter, int UnbilledMinutes);
