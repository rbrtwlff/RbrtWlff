using System;
using AkteTimer.Models;
using AkteTimer.Services;
using RvgBreakdownModel = AkteTimer.Models.RvgBreakdown;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using MessageBox = System.Windows.MessageBox;

namespace AkteTimer.ViewModels;

public sealed class BillingWizardViewModel : ViewModelBase
{
    private readonly DatabaseService _databaseService;
    private readonly BillingService _billingService;
    private readonly List<BillingCaseDisplayViewModel> _cases;
    private readonly RvgFeeTableService _rvgFeeTableService = new();
    private readonly RelayCommand _approveCommand;
    private readonly RelayCommand _saveCommand;
    private readonly RelayCommand _prevCommand;
    private readonly RelayCommand _nextCommand;
    private readonly RelayCommand _exportCommand;
    private int _currentIndex;
    private readonly long _batchId;
    private bool _isBatchFinalized;

    public BillingWizardViewModel(DatabaseService databaseService, long batchId)
    {
        _databaseService = databaseService;
        _billingService = new BillingService(databaseService);
        _batchId = batchId;
        var batch = databaseService.GetBillingBatchById(batchId);
        if (batch == null)
        {
            throw new InvalidOperationException("Abrechnungsbatch nicht gefunden.");
        }
        _isBatchFinalized = batch.FinalizedUtc.HasValue;

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
            var entries = databaseService.GetBillableEntriesForMatter(item.matter.Id);
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
                item.billingCase.SelectedEntryCount,
                item.billingCase.IncludedEntryCount,
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
                rvgEvaluation.Breakdown,
                rvgEvaluation.BreakdownNote,
                OnRvgDifferenceChanged));
        }

        _currentIndex = 0;
        _approveCommand = new RelayCommand(_ => ApproveCurrentCase(), _ => CanApproveCurrentCase());
        _saveCommand = new RelayCommand(_ => SaveCurrentCase(), _ => CanSaveCurrentCase());
        _prevCommand = new RelayCommand(_ => MovePrevious(), _ => CanMovePrevious());
        _nextCommand = new RelayCommand(_ => MoveNext(), _ => CanMoveNext());
        _exportCommand = new RelayCommand(_ => ExportAndFinalize(), _ => CanExportAndFinalize());

        NotifyPropertyChanged(nameof(CurrentIndex));
        NotifyPropertyChanged(nameof(TotalCount));
        NotifyPropertyChanged(nameof(CurrentCase));
        NotifyPropertyChanged(nameof(HeaderText));
        NotifyPropertyChanged(nameof(IsSummaryPage));
        NotifyPropertyChanged(nameof(IsCasePage));
        NotifySummaryPropertiesChanged();
    }

    public int CurrentIndex => TotalCount == 0 ? 0 : (IsSummaryPage ? TotalCount : _currentIndex + 1);

    public int TotalCount => _cases.Count;

    public BillingCaseDisplayViewModel? CurrentCase => IsSummaryPage || _cases.Count == 0 ? null : _cases[_currentIndex];

    public string HeaderText =>
        CurrentCase == null
            ? TotalCount == 0 ? "Keine Akten" : "Abschluss"
            : $"Akte {CurrentIndex}/{TotalCount} – {CurrentCase.FileRef}";

    public bool IsSummaryPage => _cases.Count > 0 && _currentIndex >= _cases.Count;

    public bool IsCasePage => !IsSummaryPage;

    public int SummaryCaseCount => _cases.Count;

    public decimal SummaryHourlyTotal => _cases.Where(item => item.IsHourlyBilling).Sum(item => item.TotalAmount);

    public decimal SummaryRvgTotal => _cases.Where(item => item.IsRvgBilling).Sum(item => item.RvgTotal);

    public decimal SummaryGrandTotal => SummaryHourlyTotal + SummaryRvgTotal;

    public bool IsBatchFinalized => _isBatchFinalized;

    public RelayCommand PrevCommand => _prevCommand;

    public RelayCommand NextCommand => _nextCommand;

    public RelayCommand ApproveCurrentCaseCommand => _approveCommand;

    public RelayCommand SaveCurrentCaseCommand => _saveCommand;

    public RelayCommand ExportCommand => _exportCommand;

    private bool CanMovePrevious() => _currentIndex > 0;

    private bool CanMoveNext() => _currentIndex < _cases.Count;

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
        NotifyPropertyChanged(nameof(IsSummaryPage));
        NotifyPropertyChanged(nameof(IsCasePage));
        NotifySummaryPropertiesChanged();
        _prevCommand.RaiseCanExecuteChanged();
        _nextCommand.RaiseCanExecuteChanged();
        _approveCommand.RaiseCanExecuteChanged();
        _saveCommand.RaiseCanExecuteChanged();
        _exportCommand.RaiseCanExecuteChanged();
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
            NotifySummaryPropertiesChanged();
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
            return new RvgBillingEvaluation(string.Empty, 0m, false, false, null, 0m, string.Empty, 0m, false, null, null);
        }

        var signature = BillingService.ComputeRvgSignature(matter);
        var snapshot = _databaseService.GetLatestRvgBillingSnapshot(matter.Id);
        if (snapshot != null && string.Equals(snapshot.Signature, signature, StringComparison.Ordinal))
        {
            var breakdown = RvgBreakdownSerializer.Deserialize(snapshot.BreakdownJson);
            var breakdownNote = breakdown == null ? "ohne Aufschlüsselung, Altbestand" : null;
            _databaseService.UpdateBillingCaseRvgData(
                billingCase.Id,
                signature,
                0m,
                false,
                snapshot.Signature,
                snapshot.Total);
            return new RvgBillingEvaluation(
                "RVG bereits abgerechnet – kein neuer Tatbestand (Freigabe gesperrt).",
                0m,
                true,
                false,
                snapshot.Signature,
                snapshot.Total,
                signature,
                0m,
                false,
                breakdown,
                breakdownNote);
        }

        var breakdownCurrent = CalculateRvgBreakdown(matter);
        var total = breakdownCurrent.Total;
        var canToggleDifference = snapshot != null;
        var useDifference = billingCase.RvgIsDifference && canToggleDifference;
        var evaluation = BuildRvgDifferenceEvaluation(signature, total, snapshot, useDifference, breakdownCurrent);
        _databaseService.UpdateBillingCaseRvgData(
            billingCase.Id,
            signature,
            evaluation.Total,
            evaluation.IsDifference,
            evaluation.BaseSignature,
            evaluation.BaseTotal);

        return evaluation;
    }

    private RvgBreakdownModel CalculateRvgBreakdown(Matter matter)
    {
        return RvgCalculator.CalculateBreakdown(matter, _rvgFeeTableService);
    }

    private RvgBillingEvaluation BuildRvgDifferenceEvaluation(
        string signature,
        decimal currentTotal,
        RvgBillingSnapshot? snapshot,
        bool useDifference,
        RvgBreakdownModel breakdown)
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
                true,
                breakdown,
                null);
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
            false,
            breakdown,
            null);
    }

    private bool CanExportAndFinalize() => !_isBatchFinalized;

    private void ExportAndFinalize()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "PDF-Datei (*.pdf)|*.pdf",
            DefaultExt = ".pdf",
            AddExtension = true,
            FileName = $"Abrechnung_{DateTime.Now:yyyyMMdd}.pdf"
        };

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            return;
        }

        try
        {
            _billingService.ExportBillingBatchToPdf(_batchId, dialog.FileName);
            _billingService.FinalizeBatch(_batchId);
            _isBatchFinalized = true;
            NotifyPropertyChanged(nameof(IsBatchFinalized));
            _exportCommand.RaiseCanExecuteChanged();
            MessageBox.Show(
                "PDF-Export abgeschlossen und Batch finalisiert.",
                "Abrechnung",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Export oder Finalisierung fehlgeschlagen: {ex.Message}",
                "Abrechnung",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private void NotifySummaryPropertiesChanged()
    {
        NotifyPropertyChanged(nameof(SummaryCaseCount));
        NotifyPropertyChanged(nameof(SummaryHourlyTotal));
        NotifyPropertyChanged(nameof(SummaryRvgTotal));
        NotifyPropertyChanged(nameof(SummaryGrandTotal));
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
        bool IsDifference,
        RvgBreakdownModel? Breakdown,
        string? BreakdownNote);
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
        int selectedEntryCount,
        int includedEntryCount,
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
        RvgBreakdownModel? rvgBreakdown,
        string? rvgBreakdownNote,
        Action<BillingCaseDisplayViewModel, bool>? onRvgDifferenceChanged)
    {
        BillingCaseId = billingCaseId;
        FileRef = fileRef;
        BillingType = billingType;
        ApprovedUtc = approvedUtc;
        TrackedMinutes = trackedMinutes;
        TrackedAmount = trackedAmount;
        HourlyRate = hourlyRate;
        SelectedEntryCount = selectedEntryCount;
        IncludedEntryCount = includedEntryCount;
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
        RvgBreakdownItems = rvgBreakdown?.Items ?? new List<RvgLineItem>();
        RvgBreakdownTotal = rvgBreakdown?.Total ?? 0m;
        _rvgBreakdownNote = rvgBreakdownNote ?? string.Empty;
        _onRvgDifferenceChanged = onRvgDifferenceChanged;
    }

    public long BillingCaseId { get; }

    public string FileRef { get; }

    public BillingType BillingType { get; }

    public DateTime? ApprovedUtc { get; private set; }

    public bool IsHourlyBilling => BillingType == BillingType.Hourly;

    public bool IsRvgBilling => BillingType == BillingType.Rvg;

    public decimal HourlyRate { get; }

    public int SelectedEntryCount { get; }

    public int IncludedEntryCount { get; }

    public int AutoIncludedEntryCount => Math.Max(0, IncludedEntryCount - SelectedEntryCount);

    public string IncludedEntriesText => $"Einbezogen: {IncludedEntryCount} (davon automatisch: {AutoIncludedEntryCount})";

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

    public IReadOnlyList<RvgLineItem> RvgBreakdownItems { get; }

    public decimal RvgBreakdownTotal { get; }

    public bool HasRvgBreakdown => RvgBreakdownItems.Count > 0;

    public string RvgBreakdownNote => _rvgBreakdownNote;

    public bool HasRvgBreakdownNote => !string.IsNullOrWhiteSpace(_rvgBreakdownNote);

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
    private string _rvgBreakdownNote = string.Empty;

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
