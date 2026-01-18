using AkteTimer.Models;
using AkteTimer.Services;

namespace AkteTimer.ViewModels;

public sealed class MatterDetailsViewModel : ViewModelBase
{
    private readonly TimeEntryService _timeEntryService;
    private readonly RvgFeeTableService _rvgFeeTableService;
    private readonly Matter _matter;
    private BillingType _billingType;
    private decimal _subjectValueEur;
    private decimal _feeFactor;
    private decimal _targetRateEurPerHour;
    private int _actualMinutes;
    private decimal _fee1_0Eur;
    private decimal _rvgEstimateEur;
    private decimal? _effectiveHourlyRateEur;
    private TimeSpan? _breakEvenTime;

    public MatterDetailsViewModel(Matter matter, TimeEntryService timeEntryService, RvgFeeTableService rvgFeeTableService)
    {
        _matter = matter;
        _timeEntryService = timeEntryService;
        _rvgFeeTableService = rvgFeeTableService;
        _billingType = matter.BillingType;
        _subjectValueEur = matter.SubjectValueEur;
        _feeFactor = matter.FeeFactor;
        _targetRateEurPerHour = matter.TargetRateEurPerHour;
        _actualMinutes = GetActualMinutes();
        RefreshCalculations();
    }

    public string FileRef => _matter.FileRef;

    public BillingType BillingType
    {
        get => _billingType;
        set
        {
            if (_billingType == value)
            {
                return;
            }

            _billingType = value;
            _matter.BillingType = value;
            _timeEntryService.UpdateMatter(_matter);
            NotifyPropertyChanged();
            NotifyPropertyChanged(nameof(ShowRvgFields));
        }
    }

    public decimal SubjectValueEur
    {
        get => _subjectValueEur;
        set
        {
            if (_subjectValueEur == value)
            {
                return;
            }

            _subjectValueEur = Math.Max(0m, value);
            _matter.SubjectValueEur = _subjectValueEur;
            _timeEntryService.UpdateMatter(_matter);
            NotifyPropertyChanged();
            RefreshCalculations();
        }
    }

    public decimal FeeFactor
    {
        get => _feeFactor;
        set
        {
            var clamped = Math.Clamp(value, 0.1m, 2.5m);
            if (_feeFactor == clamped)
            {
                return;
            }

            _feeFactor = clamped;
            _matter.FeeFactor = _feeFactor;
            _timeEntryService.UpdateMatter(_matter);
            NotifyPropertyChanged();
            RefreshCalculations();
        }
    }

    public decimal TargetRateEurPerHour
    {
        get => _targetRateEurPerHour;
        set
        {
            if (_targetRateEurPerHour == value)
            {
                return;
            }

            _targetRateEurPerHour = Math.Max(0m, value);
            _matter.TargetRateEurPerHour = _targetRateEurPerHour;
            _timeEntryService.UpdateMatter(_matter);
            NotifyPropertyChanged();
            RefreshCalculations();
        }
    }

    public decimal Fee1_0Eur => _fee1_0Eur;

    public decimal RvgEstimateEur => _rvgEstimateEur;

    public string EffectiveHourlyRateText => _effectiveHourlyRateEur?.ToString("N2") ?? "-";

    public string BreakEvenTimeText => _breakEvenTime == null ? "-" : RvgCalculator.FormatBreakEvenTime(_breakEvenTime.Value);

    public string Fee1_0EurText => _fee1_0Eur.ToString("N2");

    public string RvgEstimateEurText => _rvgEstimateEur.ToString("N2");

    public bool ShowRvgFields => BillingType == BillingType.Rvg;

    public string ActualMinutesText => _actualMinutes.ToString();

    private int GetActualMinutes()
    {
        var entries = _timeEntryService.GetEntriesForMatter(_matter.Id);
        var totalMinutes = 0;
        foreach (var entry in entries)
        {
            var duration = TimeEntryCalculations.GetDuration(entry);
            totalMinutes += TimeEntryCalculations.GetActualMinutes(duration);
        }

        return totalMinutes;
    }

    private void RefreshCalculations()
    {
        _fee1_0Eur = _rvgFeeTableService.LookupFee1_0(_subjectValueEur);
        _rvgEstimateEur = RvgCalculator.CalculateEstimate(_fee1_0Eur, _feeFactor);
        var actualHours = _actualMinutes / 60m;
        _effectiveHourlyRateEur = RvgCalculator.CalculateEffectiveHourlyRate(_rvgEstimateEur, actualHours);
        var targetRate = _timeEntryService.GetEffectiveTargetRate(_targetRateEurPerHour);
        _breakEvenTime = RvgCalculator.CalculateBreakEvenTime(_rvgEstimateEur, targetRate);
        NotifyPropertyChanged(nameof(Fee1_0Eur));
        NotifyPropertyChanged(nameof(RvgEstimateEur));
        NotifyPropertyChanged(nameof(EffectiveHourlyRateText));
        NotifyPropertyChanged(nameof(BreakEvenTimeText));
        NotifyPropertyChanged(nameof(Fee1_0EurText));
        NotifyPropertyChanged(nameof(RvgEstimateEurText));
    }
}
