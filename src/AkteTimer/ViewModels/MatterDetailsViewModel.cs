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
    private decimal? _feeFactor;
    private decimal _targetRateEurPerHour;
    private decimal _hourlyRateEurPerHour;
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
        _hourlyRateEurPerHour = matter.HourlyRateEurPerHour;
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

    public decimal? FeeFactor
    {
        get => _feeFactor;
        set
        {
            var normalized = NormalizeFeeFactor(value);
            if (_feeFactor == normalized)
            {
                return;
            }

            _feeFactor = normalized;
            _matter.FeeFactor = normalized;
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

    public decimal HourlyRateEurPerHour
    {
        get => _hourlyRateEurPerHour;
        set
        {
            if (_hourlyRateEurPerHour == value)
            {
                return;
            }

            _hourlyRateEurPerHour = Math.Max(0m, value);
            _matter.HourlyRateEurPerHour = _hourlyRateEurPerHour;
            _timeEntryService.UpdateMatter(_matter);
            NotifyPropertyChanged();
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
        var feeModifierSum = RvgCalculator.CalculateFeeModifierSum(
            _matter.BusinessFee13Enabled,
            _matter.TermFee12Enabled,
            _matter.SettlementFee10Enabled,
            _matter.SettlementFee15Enabled);
        _rvgEstimateEur = RvgCalculator.CalculateEstimate(_fee1_0Eur, _feeFactor ?? 0m, feeModifierSum);
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

    private static decimal? NormalizeFeeFactor(decimal? value)
    {
        if (value == null)
        {
            return null;
        }

        var clamped = Math.Clamp(value.Value, 0m, 3m);
        return Math.Round(clamped, 1, MidpointRounding.AwayFromZero);
    }
}
