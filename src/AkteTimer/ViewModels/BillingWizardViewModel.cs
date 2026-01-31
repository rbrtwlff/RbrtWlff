using System;
using AkteTimer.Models;
using AkteTimer.Services;
using MessageBox = System.Windows.MessageBox;

namespace AkteTimer.ViewModels;

public sealed class BillingWizardViewModel : ViewModelBase
{
    private readonly DatabaseService _databaseService;
    private readonly List<BillingCaseDisplayViewModel> _cases;
    private readonly RelayCommand _approveCommand;
    private readonly RelayCommand _saveCommand;
    private readonly RelayCommand _prevCommand;
    private readonly RelayCommand _nextCommand;
    private int _currentIndex;

    public BillingWizardViewModel(DatabaseService databaseService, long batchId)
    {
        _databaseService = databaseService;
        if (databaseService.GetBillingBatchById(batchId) == null)
        {
            throw new InvalidOperationException("Abrechnungsbatch nicht gefunden.");
        }

        var caseItems = databaseService.GetBillingCasesForBatch(batchId)
            .Select(billingCase =>
            {
                var matter = databaseService.GetMatterById(billingCase.MatterId)
                             ?? throw new InvalidOperationException("Akte nicht gefunden.");
                return new { billingCase, matter };
            })
            .OrderBy(item => item.matter.FileRef, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _cases = new List<BillingCaseDisplayViewModel>();
        foreach (var item in caseItems)
        {
            var entries = databaseService.GetEntriesForMatter(item.matter.Id);
            var timeEntryViewModels = entries
                .Select(entry => new TimeEntryRowViewModel(entry))
                .ToList();

            var adjustment = databaseService.GetBillingAdjustmentForCase(item.billingCase.Id);
            _cases.Add(new BillingCaseDisplayViewModel(
                item.billingCase.Id,
                item.matter.FileRef,
                item.billingCase.BillingType,
                item.billingCase.ApprovedUtc,
                item.billingCase.TrackedMinutes,
                item.billingCase.TrackedAmount,
                item.billingCase.DummyMinutes,
                item.billingCase.DummyAmount,
                item.billingCase.TotalMinutes,
                item.billingCase.TotalAmount,
                item.matter.HourlyRateEurPerHour,
                adjustment?.Reason,
                timeEntryViewModels));
        }

        _currentIndex = 0;
        _approveCommand = new RelayCommand(_ => ApproveCurrentCase(), _ => CanApproveCurrentCase());
        _saveCommand = new RelayCommand(_ => SaveCurrentCase(), _ => CanSaveCurrentCase());
        _prevCommand = new RelayCommand(_ => MovePrevious(), _ => CanMovePrevious());
        _nextCommand = new RelayCommand(_ => MoveNext(), _ => CanMoveNext());

        NotifyPropertyChanged(nameof(CurrentIndex));
        NotifyPropertyChanged(nameof(TotalCount));
        NotifyPropertyChanged(nameof(CurrentCase));
        NotifyPropertyChanged(nameof(HeaderText));
    }

    public int CurrentIndex => TotalCount == 0 ? 0 : _currentIndex + 1;

    public int TotalCount => _cases.Count;

    public BillingCaseDisplayViewModel? CurrentCase => _cases.Count == 0 ? null : _cases[_currentIndex];

    public string HeaderText =>
        CurrentCase == null
            ? "Keine Akten"
            : $"Akte {CurrentIndex}/{TotalCount} – {CurrentCase.FileRef}";

    public RelayCommand PrevCommand => _prevCommand;

    public RelayCommand NextCommand => _nextCommand;

    public RelayCommand ApproveCurrentCaseCommand => _approveCommand;

    public RelayCommand SaveCurrentCaseCommand => _saveCommand;

    private bool CanMovePrevious() => _currentIndex > 0;

    private bool CanMoveNext() => _currentIndex < _cases.Count - 1;

    private bool CanApproveCurrentCase() => CurrentCase != null && !CurrentCase.IsApproved;

    private bool CanSaveCurrentCase() => CurrentCase != null && CurrentCase.IsHourlyBilling;

    private void ApproveCurrentCase()
    {
        if (CurrentCase == null || CurrentCase.IsApproved)
        {
            return;
        }

        if (!SaveCurrentCase())
        {
            return;
        }

        var approvedUtc = DateTime.UtcNow;
        _databaseService.UpdateBillingCaseApprovedUtc(CurrentCase.BillingCaseId, approvedUtc);
        CurrentCase.SetApprovedUtc(approvedUtc);
        NotifyPropertyChanged(nameof(CurrentCase));
        _approveCommand.RaiseCanExecuteChanged();
    }

    private void MovePrevious()
    {
        if (!CanMovePrevious())
        {
            return;
        }

        if (!SaveCurrentCase())
        {
            return;
        }

        _currentIndex--;
        UpdateNavigationState();
    }

    private void MoveNext()
    {
        if (!CanMoveNext())
        {
            return;
        }

        if (CurrentCase != null && !CurrentCase.IsApproved)
        {
            var result = MessageBox.Show(
                "Nicht freigegeben – trotzdem weiter?",
                "Abrechnung",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning,
                System.Windows.MessageBoxResult.No);
            if (result != System.Windows.MessageBoxResult.Yes)
            {
                return;
            }
        }

        if (!SaveCurrentCase())
        {
            return;
        }

        _currentIndex++;
        UpdateNavigationState();
    }

    private void UpdateNavigationState()
    {
        NotifyPropertyChanged(nameof(CurrentIndex));
        NotifyPropertyChanged(nameof(CurrentCase));
        NotifyPropertyChanged(nameof(HeaderText));
        _prevCommand.RaiseCanExecuteChanged();
        _nextCommand.RaiseCanExecuteChanged();
        _approveCommand.RaiseCanExecuteChanged();
        _saveCommand.RaiseCanExecuteChanged();
    }

    private bool SaveCurrentCase()
    {
        if (CurrentCase == null || !CurrentCase.IsHourlyBilling)
        {
            return true;
        }

        try
        {
            var minutesDelta = CurrentCase.CalculateMinutesDelta();
            var dummyAmount = CurrentCase.CalculateDummyAmount(minutesDelta);
            var totalMinutes = CurrentCase.TrackedMinutes + minutesDelta;
            var totalAmount = CurrentCase.TrackedAmount + dummyAmount;

            CurrentCase.ApplyAdjustment(minutesDelta, dummyAmount, totalMinutes, totalAmount);

            _databaseService.SaveBillingAdjustmentForCase(
                CurrentCase.BillingCaseId,
                minutesDelta,
                dummyAmount,
                CurrentCase.DummyReason,
                CurrentCase.DummyMinutes,
                CurrentCase.DummyAmount,
                CurrentCase.TotalMinutes,
                CurrentCase.TotalAmount);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Der Dummy/Nachtrag konnte nicht gespeichert werden: {ex.Message}",
                "Abrechnung",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            return false;
        }
    }
}

public sealed class BillingCaseDisplayViewModel : ViewModelBase
{
    public BillingCaseDisplayViewModel(
        long billingCaseId,
        string fileRef,
        BillingType billingType,
        DateTime? approvedUtc,
        int trackedMinutes,
        decimal trackedAmount,
        int dummyMinutes,
        decimal dummyAmount,
        int totalMinutes,
        decimal totalAmount,
        decimal hourlyRate,
        string? dummyReason,
        IReadOnlyList<TimeEntryRowViewModel> timeEntries)
    {
        BillingCaseId = billingCaseId;
        FileRef = fileRef;
        BillingType = billingType;
        ApprovedUtc = approvedUtc;
        TrackedMinutes = trackedMinutes;
        TrackedAmount = trackedAmount;
        HourlyRate = hourlyRate;
        _dummyMinutes = dummyMinutes;
        _dummyAmount = dummyAmount;
        _totalMinutes = totalMinutes;
        _totalAmount = totalAmount;
        _dummyHours = dummyMinutes / 60m;
        _dummyReason = dummyReason ?? string.Empty;
        TimeEntries = timeEntries;
    }

    public long BillingCaseId { get; }

    public string FileRef { get; }

    public BillingType BillingType { get; }

    public DateTime? ApprovedUtc { get; private set; }

    public bool IsHourlyBilling => BillingType == BillingType.Hourly;

    public decimal HourlyRate { get; }

    public bool IsApproved => ApprovedUtc.HasValue;

    public string ApprovalStatusText => ApprovedUtc.HasValue ? "freigegeben" : "nicht freigegeben";

    public IReadOnlyList<TimeEntryRowViewModel> TimeEntries { get; }

    public int TrackedMinutes { get; }

    public decimal TrackedAmount { get; }

    public decimal TrackedHours => TrackedMinutes / 60m;

    public int DummyMinutes => _dummyMinutes;

    public decimal DummyAmount => _dummyAmount;

    public int TotalMinutes => _totalMinutes;

    public decimal TotalAmount => _totalAmount;

    public decimal DummyHours
    {
        get => _dummyHours;
        set => ApplyDummyHours(value, keepTarget: false);
    }

    public decimal? TargetTotalHours
    {
        get => _targetTotalHours;
        set
        {
            if (_targetTotalHours == value)
            {
                return;
            }

            _targetTotalHours = value;
            NotifyPropertyChanged();
            NotifyPropertyChanged(nameof(IsDummyHoursEditable));
            NotifyPropertyChanged(nameof(DummyRowNote));

            if (_targetTotalHours.HasValue)
            {
                var diff = _targetTotalHours.Value - TrackedHours;
                ApplyDummyHours(diff, keepTarget: true);
            }
            else
            {
                RecalculateTotals();
            }
        }
    }

    public bool IsDummyHoursEditable => !_targetTotalHours.HasValue;

    public string DummyReason
    {
        get => _dummyReason;
        set
        {
            var next = value ?? string.Empty;
            if (_dummyReason == next)
            {
                return;
            }

            _dummyReason = next;
            NotifyPropertyChanged();
            NotifyPropertyChanged(nameof(DummyRowNote));
        }
    }

    public string DummyRowLabel => "DUMMY/NACHTRAG";

    public decimal DummyHoursDisplay => DummyMinutes / 60m;

    public string DummyDurationText => TimeSpan.FromMinutes(DummyMinutes).ToString(@"hh\:mm");

    public string DummyRowNote
    {
        get
        {
            var reason = string.IsNullOrWhiteSpace(DummyReason) ? "ohne Begründung" : DummyReason;
            return TargetTotalHours.HasValue
                ? $"Zielsumme {TargetTotalHours.Value:N2} h – {reason}"
                : reason;
        }
    }

    private int _dummyMinutes;
    private decimal _dummyAmount;
    private int _totalMinutes;
    private decimal _totalAmount;
    private decimal _dummyHours;
    private decimal? _targetTotalHours;
    private string _dummyReason = string.Empty;

    public int CalculateMinutesDelta()
    {
        var hoursDelta = _targetTotalHours.HasValue
            ? _targetTotalHours.Value - TrackedHours
            : _dummyHours;
        return (int)Math.Round(hoursDelta * 60m, MidpointRounding.AwayFromZero);
    }

    public decimal CalculateDummyAmount(int minutesDelta)
    {
        return (minutesDelta / 60m) * HourlyRate;
    }

    public void ApplyAdjustment(int minutesDelta, decimal dummyAmount, int totalMinutes, decimal totalAmount)
    {
        _dummyMinutes = minutesDelta;
        _dummyAmount = dummyAmount;
        _totalMinutes = totalMinutes;
        _totalAmount = totalAmount;
        NotifyPropertyChanged(nameof(DummyMinutes));
        NotifyPropertyChanged(nameof(DummyAmount));
        NotifyPropertyChanged(nameof(TotalMinutes));
        NotifyPropertyChanged(nameof(TotalAmount));
        NotifyPropertyChanged(nameof(DummyHoursDisplay));
        NotifyPropertyChanged(nameof(DummyDurationText));
    }

    public void SetApprovedUtc(DateTime approvedUtc)
    {
        ApprovedUtc = approvedUtc;
    }

    private void ApplyDummyHours(decimal value, bool keepTarget)
    {
        if (_dummyHours == value)
        {
            return;
        }

        _dummyHours = value;
        if (!keepTarget && _targetTotalHours.HasValue)
        {
            _targetTotalHours = null;
            NotifyPropertyChanged(nameof(TargetTotalHours));
            NotifyPropertyChanged(nameof(IsDummyHoursEditable));
            NotifyPropertyChanged(nameof(DummyRowNote));
        }

        NotifyPropertyChanged(nameof(DummyHours));
        RecalculateTotals();
    }

    private void RecalculateTotals()
    {
        var minutesDelta = CalculateMinutesDelta();
        var dummyAmount = CalculateDummyAmount(minutesDelta);
        var totalMinutes = TrackedMinutes + minutesDelta;
        var totalAmount = TrackedAmount + dummyAmount;
        ApplyAdjustment(minutesDelta, dummyAmount, totalMinutes, totalAmount);
    }
}

public sealed class TimeEntryRowViewModel
{
    public TimeEntryRowViewModel(TimeEntry entry)
    {
        StartLocal = entry.StartUtc.ToLocalTime();
        DurationText = TimeEntryCalculations.GetDuration(entry).ToString(@"hh\:mm");
        Note = entry.Note ?? string.Empty;
    }

    public DateTime StartLocal { get; }

    public string DurationText { get; }

    public string Note { get; }
}
