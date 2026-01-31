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

            _cases.Add(new BillingCaseDisplayViewModel(
                item.billingCase.Id,
                item.matter.FileRef,
                item.billingCase.BillingType,
                item.billingCase.ApprovedUtc,
                item.billingCase.TrackedMinutes,
                item.billingCase.TrackedAmount,
                item.billingCase.TotalMinutes,
                item.billingCase.TotalAmount,
                timeEntryViewModels));
        }

        _currentIndex = 0;
        _approveCommand = new RelayCommand(_ => ApproveCurrentCase(), _ => CanApproveCurrentCase());
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

    private bool CanMovePrevious() => _currentIndex > 0;

    private bool CanMoveNext() => _currentIndex < _cases.Count - 1;

    private bool CanApproveCurrentCase() => CurrentCase != null && !CurrentCase.IsApproved;

    private void ApproveCurrentCase()
    {
        if (CurrentCase == null || CurrentCase.IsApproved)
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
    }
}

public sealed class BillingCaseDisplayViewModel
{
    public BillingCaseDisplayViewModel(
        long billingCaseId,
        string fileRef,
        BillingType billingType,
        DateTime? approvedUtc,
        int trackedMinutes,
        decimal trackedAmount,
        int totalMinutes,
        decimal totalAmount,
        IReadOnlyList<TimeEntryRowViewModel> timeEntries)
    {
        BillingCaseId = billingCaseId;
        FileRef = fileRef;
        BillingType = billingType;
        ApprovedUtc = approvedUtc;
        TrackedMinutes = trackedMinutes;
        TrackedAmount = trackedAmount;
        TotalMinutes = totalMinutes;
        TotalAmount = totalAmount;
        TimeEntries = timeEntries;
    }

    public long BillingCaseId { get; }

    public string FileRef { get; }

    public BillingType BillingType { get; }

    public DateTime? ApprovedUtc { get; private set; }

    public bool IsApproved => ApprovedUtc.HasValue;

    public string ApprovalStatusText => ApprovedUtc.HasValue ? "freigegeben" : "nicht freigegeben";

    public IReadOnlyList<TimeEntryRowViewModel> TimeEntries { get; }

    public int TrackedMinutes { get; }

    public decimal TrackedAmount { get; }

    public int TotalMinutes { get; }

    public decimal TotalAmount { get; }

    public void SetApprovedUtc(DateTime approvedUtc)
    {
        ApprovedUtc = approvedUtc;
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
