using System;
using AkteTimer.Models;
using AkteTimer.Services;
using MessageBox = System.Windows.MessageBox;

namespace AkteTimer.ViewModels;

public sealed class BillingWizardViewModel : ViewModelBase
{
    private readonly DatabaseService _databaseService;
    private readonly List<BillingCaseDisplayViewModel> _cases;
    private readonly RvgFeeTableService _rvgFeeTableService = new();
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
            var rvgEvaluation = EvaluateRvgBilling(item.billingCase, item.matter);
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
                timeEntryViewModels,
                rvgEvaluation.StatusText,
                rvgEvaluation.Total,
                rvgEvaluation.IsApprovalBlocked,
                rvgEvaluation.CanToggleDifference,
                rvgEvaluation.IsDifference,
                rvgEvaluation.BaseSignature,
                rvgEvaluation.BaseTotal,
                rvgEvaluation.CurrentSignature,
                rvgEvaluation.CurrentTotal,
                OnRvgDifferenceChanged));
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

    private bool CanApproveCurrentCase() =>
        CurrentCase != null && !CurrentCase.IsApproved && !CurrentCase.IsRvgApprovalBlocked;

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

    private void OnRvgDifferenceChanged(BillingCaseDisplayViewModel caseViewModel, bool isDifferenceEnabled)
    {
        if (!caseViewModel.IsRvgBilling || !caseViewModel.CanToggleRvgDifference)
        {
            return;
        }

        if (isDifferenceEnabled)
        {
            var delta = caseViewModel.RvgCurrentTotal - caseViewModel.RvgBaseTotal;
            var hasNegativeDelta = delta < 0m;
            var safeDelta = hasNegativeDelta ? 0m : delta;
            var statusText = hasNegativeDelta
                ? "RVG Differenz negativ – Freigabe gesperrt."
                : "RVG Differenz wird abgerechnet.";
            if (hasNegativeDelta)
            {
                MessageBox.Show(
                    "Die RVG-Differenz ist negativ. Die Freigabe wurde gesperrt.",
                    "Abrechnung",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }

            caseViewModel.UpdateRvgDifferenceState(
                statusText,
                safeDelta,
                hasNegativeDelta,
                true,
                caseViewModel.RvgBaseSignature,
                caseViewModel.RvgBaseTotal,
                caseViewModel.RvgCurrentSignature,
                caseViewModel.RvgCurrentTotal);
            _databaseService.UpdateBillingCaseRvgData(
                caseViewModel.BillingCaseId,
                caseViewModel.RvgCurrentSignature,
                safeDelta,
                true,
                caseViewModel.RvgBaseSignature,
                caseViewModel.RvgBaseTotal);
        }
        else
        {
            var statusText = caseViewModel.RvgBaseSignature == null
                ? "RVG bereit zur Abrechnung."
                : "RVG neuer Tatbestand – Freigabe möglich.";
            caseViewModel.UpdateRvgDifferenceState(
                statusText,
                caseViewModel.RvgCurrentTotal,
                false,
                false,
                caseViewModel.RvgBaseSignature,
                caseViewModel.RvgBaseTotal,
                caseViewModel.RvgCurrentSignature,
                caseViewModel.RvgCurrentTotal);
            _databaseService.UpdateBillingCaseRvgData(
                caseViewModel.BillingCaseId,
                caseViewModel.RvgCurrentSignature,
                caseViewModel.RvgCurrentTotal,
                false,
                null,
                0m);
        }

        NotifyPropertyChanged(nameof(CurrentCase));
        _approveCommand.RaiseCanExecuteChanged();
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

    private RvgBillingEvaluation EvaluateRvgBilling(BillingCase billingCase, Matter matter)
    {
        if (billingCase.BillingType != BillingType.Rvg)
        {
            return new RvgBillingEvaluation(string.Empty, 0m, false, false, null, 0m, string.Empty, 0m, false);
        }

        var signature = BillingService.ComputeRvgSignature(matter);
        var snapshot = _databaseService.GetLatestRvgBillingSnapshot(matter.Id);
        if (snapshot != null && string.Equals(snapshot.Signature, signature, StringComparison.Ordinal))
        {
            return new RvgBillingEvaluation(
                "RVG bereits abgerechnet – kein neuer Tatbestand (Freigabe gesperrt).",
                0m,
                true,
                false,
                snapshot.Signature,
                snapshot.Total,
                signature,
                0m,
                false);
        }

        var total = CalculateRvgTotal(matter);
        var canToggleDifference = snapshot != null;
        var useDifference = billingCase.RvgIsDifference && canToggleDifference;
        var evaluation = BuildRvgDifferenceEvaluation(signature, total, snapshot, useDifference);
        _databaseService.UpdateBillingCaseRvgData(
            billingCase.Id,
            signature,
            evaluation.Total,
            evaluation.IsDifference,
            evaluation.BaseSignature,
            evaluation.BaseTotal);

        return evaluation;
    }

    private decimal CalculateRvgTotal(Matter matter)
    {
        var fee1_0 = _rvgFeeTableService.LookupFee1_0(matter.SubjectValueEur);
        var businessFee = matter.BusinessFee13Enabled ? RvgCalculator.RoundCurrency(fee1_0 * 1.3m) : 0m;
        var termFee = matter.TermFee12Enabled ? RvgCalculator.RoundCurrency(fee1_0 * 1.2m) : 0m;
        var settlement10Fee = matter.SettlementFee10Enabled ? RvgCalculator.RoundCurrency(fee1_0 * 1.0m) : 0m;
        var settlement15Fee = matter.SettlementFee15Enabled ? RvgCalculator.RoundCurrency(fee1_0 * 1.5m) : 0m;
        var customFee = matter.CustomFeeFactor.HasValue
            ? RvgCalculator.RoundCurrency(fee1_0 * matter.CustomFeeFactor.Value)
            : 0m;
        return RvgCalculator.RoundCurrency(businessFee + termFee + settlement10Fee + settlement15Fee + customFee);
    }

    private RvgBillingEvaluation BuildRvgDifferenceEvaluation(
        string signature,
        decimal currentTotal,
        RvgBillingSnapshot? snapshot,
        bool useDifference)
    {
        var canToggleDifference = snapshot != null;
        if (useDifference && snapshot != null)
        {
            var delta = currentTotal - snapshot.Total;
            var hasNegativeDelta = delta < 0m;
            var safeDelta = hasNegativeDelta ? 0m : delta;
            var statusText = hasNegativeDelta
                ? "RVG Differenz negativ – Freigabe gesperrt."
                : "RVG Differenz wird abgerechnet.";
            return new RvgBillingEvaluation(
                statusText,
                safeDelta,
                hasNegativeDelta,
                canToggleDifference,
                snapshot.Signature,
                snapshot.Total,
                signature,
                currentTotal,
                true);
        }

        var defaultStatusText = snapshot == null
            ? "RVG bereit zur Abrechnung."
            : "RVG neuer Tatbestand – Freigabe möglich.";

        return new RvgBillingEvaluation(
            defaultStatusText,
            currentTotal,
            false,
            canToggleDifference,
            snapshot?.Signature,
            snapshot?.Total ?? 0m,
            signature,
            currentTotal,
            false);
    }

    private readonly record struct RvgBillingEvaluation(
        string StatusText,
        decimal Total,
        bool IsApprovalBlocked,
        bool CanToggleDifference,
        string? BaseSignature,
        decimal BaseTotal,
        string CurrentSignature,
        decimal CurrentTotal,
        bool IsDifference);
}

public sealed class BillingCaseDisplayViewModel : ViewModelBase
{
    private readonly Action<BillingCaseDisplayViewModel, bool>? _onRvgDifferenceChanged;

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
        IReadOnlyList<TimeEntryRowViewModel> timeEntries,
        string rvgStatusText,
        decimal rvgTotal,
        bool isRvgApprovalBlocked,
        bool canToggleRvgDifference,
        bool isRvgDifferenceEnabled,
        string? rvgBaseSignature,
        decimal rvgBaseTotal,
        string rvgCurrentSignature,
        decimal rvgCurrentTotal,
        Action<BillingCaseDisplayViewModel, bool>? onRvgDifferenceChanged)
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
        _rvgStatusText = rvgStatusText;
        _rvgTotal = rvgTotal;
        _isRvgApprovalBlocked = isRvgApprovalBlocked;
        CanToggleRvgDifference = canToggleRvgDifference;
        _isRvgDifferenceEnabled = isRvgDifferenceEnabled;
        RvgBaseSignature = rvgBaseSignature;
        RvgBaseTotal = rvgBaseTotal;
        RvgCurrentSignature = rvgCurrentSignature;
        RvgCurrentTotal = rvgCurrentTotal;
        _onRvgDifferenceChanged = onRvgDifferenceChanged;
    }

    public long BillingCaseId { get; }

    public string FileRef { get; }

    public BillingType BillingType { get; }

    public DateTime? ApprovedUtc { get; private set; }

    public bool IsHourlyBilling => BillingType == BillingType.Hourly;

    public bool IsRvgBilling => BillingType == BillingType.Rvg;

    public decimal HourlyRate { get; }

    public bool IsApproved => ApprovedUtc.HasValue;

    public string ApprovalStatusText => ApprovedUtc.HasValue ? "freigegeben" : "nicht freigegeben";

    public string RvgStatusText => _rvgStatusText;

    public bool IsRvgApprovalBlocked => _isRvgApprovalBlocked;

    public decimal RvgTotal => _rvgTotal;

    public bool CanToggleRvgDifference { get; }

    public bool IsRvgDifferenceEnabled
    {
        get => _isRvgDifferenceEnabled;
        set
        {
            if (_isRvgDifferenceEnabled == value)
            {
                return;
            }

            _isRvgDifferenceEnabled = value;
            NotifyPropertyChanged();
            _onRvgDifferenceChanged?.Invoke(this, value);
        }
    }

    public string? RvgBaseSignature { get; private set; }

    public decimal RvgBaseTotal { get; private set; }

    public string RvgCurrentSignature { get; private set; }

    public decimal RvgCurrentTotal { get; private set; }

    public bool ShowRvgTotal => IsRvgBilling && !IsRvgApprovalBlocked;

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
    private string _rvgStatusText = string.Empty;
    private decimal _rvgTotal;
    private bool _isRvgApprovalBlocked;
    private bool _isRvgDifferenceEnabled;

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

    public void UpdateRvgDifferenceState(
        string statusText,
        decimal rvgTotal,
        bool isApprovalBlocked,
        bool isDifferenceEnabled,
        string? baseSignature,
        decimal baseTotal,
        string currentSignature,
        decimal currentTotal)
    {
        _rvgStatusText = statusText;
        _rvgTotal = rvgTotal;
        _isRvgApprovalBlocked = isApprovalBlocked;
        _isRvgDifferenceEnabled = isDifferenceEnabled;
        RvgBaseSignature = baseSignature;
        RvgBaseTotal = baseTotal;
        RvgCurrentSignature = currentSignature;
        RvgCurrentTotal = currentTotal;
        NotifyPropertyChanged(nameof(RvgStatusText));
        NotifyPropertyChanged(nameof(RvgTotal));
        NotifyPropertyChanged(nameof(IsRvgApprovalBlocked));
        NotifyPropertyChanged(nameof(IsRvgDifferenceEnabled));
        NotifyPropertyChanged(nameof(ShowRvgTotal));
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
