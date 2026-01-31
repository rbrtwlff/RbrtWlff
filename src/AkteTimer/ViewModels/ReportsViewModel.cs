using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using AkteTimer.Models;
using AkteTimer.Services;
using AkteTimer.Views;
using ClosedXML.Excel;
using Microsoft.Win32;
using MessageBox = System.Windows.MessageBox;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace AkteTimer.ViewModels;

public sealed class ReportsViewModel : ViewModelBase
{
    private readonly TimeEntryService _timeEntryService;
    private readonly DatabaseService _databaseService;
    private readonly BillingService _billingService;
    private readonly RvgFeeTableService _rvgFeeTableService = new();
    private DateTime _fromDate;
    private DateTime _toDate;
    private string _todayTotalDuration = "00:00:00";
    private int _todayTotalMinutes;
    private int _todayTotalRoundedMinutes;
    private string _rangeTotalDuration = "00:00:00";
    private int _rangeTotalMinutes;
    private int _rangeTotalRoundedMinutes;
    private string _matterTotalDuration = "00:00:00";
    private int _matterTotalMinutes;
    private int _matterTotalRoundedMinutes;
    private readonly RelayCommand _exportCsvCommand;
    private readonly RelayCommand _exportExcelCommand;
    private readonly RelayCommand _resetMatterFilterCommand;
    private readonly RelayCommand _deleteEntryCommand;
    private readonly RelayCommand _createBillingDraftsFromViewCommand;
    private readonly RelayCommand _createBillingDraftsFromAllOpenCommand;
    private string _matterFilterSearchText = string.Empty;
    private MatterFilterItem? _selectedMatterFilter;
    private ICollectionView? _matterFilterView;
    private string _autoBillingHint = string.Empty;
    private bool _showAutoBillingHint;
    private bool _showDescription;
    private List<TimeEntry> _rangeEntries = new();

    public ReportsViewModel(TimeEntryService timeEntryService, DatabaseService databaseService, BillingService billingService)
    {
        _timeEntryService = timeEntryService;
        _databaseService = databaseService;
        _billingService = billingService;
        _fromDate = DateTime.Today.AddDays(-6);
        _toDate = DateTime.Today;

        foreach (var matter in _timeEntryService.GetAllMatters())
        {
            MatterFilters.Add(new MatterFilterItem(matter));
        }

        _exportCsvCommand = new RelayCommand(_ => ExportCsv(), _ => CanExport());
        _exportExcelCommand = new RelayCommand(_ => ExportExcel(), _ => CanExport());
        _resetMatterFilterCommand = new RelayCommand(_ => ResetMatterFilter(), _ => CanResetMatterFilter());
        _deleteEntryCommand = new RelayCommand(DeleteEntry);
        _createBillingDraftsFromViewCommand = new RelayCommand(_ => CreateBillingDraftsFromView());
        _createBillingDraftsFromAllOpenCommand = new RelayCommand(_ => CreateBillingDraftsFromAllOpen());

        _matterFilterView = CollectionViewSource.GetDefaultView(MatterFilters);
        _matterFilterView.Filter = FilterMatter;

        RefreshToday();
        RefreshRangeAndMatters();
    }

    public ObservableCollection<ReportEntryViewModel> TodayEntries { get; } = new();

    public ObservableCollection<DayGroupViewModel> TodayGroups { get; } = new();

    public ObservableCollection<MatterFilterItem> MatterFilters { get; } = new();

    public ICollectionView MatterFilterView => _matterFilterView ??= CollectionViewSource.GetDefaultView(MatterFilters);

    public ObservableCollection<DayGroupViewModel> RangeGroups { get; } = new();

    public ObservableCollection<MatterGroupViewModel> MatterGroups { get; } = new();

    public ObservableCollection<DeleteEntryViewModel> DeleteEntries { get; } = new();


    public RelayCommand ExportCsvCommand => _exportCsvCommand;

    public RelayCommand ExportExcelCommand => _exportExcelCommand;

    public RelayCommand ResetMatterFilterCommand => _resetMatterFilterCommand;

    public RelayCommand DeleteEntryCommand => _deleteEntryCommand;

    public ICommand CreateBillingDraftsFromViewCommand => _createBillingDraftsFromViewCommand;

    public ICommand CreateBillingDraftsFromAllOpenCommand => _createBillingDraftsFromAllOpenCommand;

    public string AutoBillingHint
    {
        get => _autoBillingHint;
        private set
        {
            if (_autoBillingHint == value)
            {
                return;
            }

            _autoBillingHint = value;
            NotifyPropertyChanged();
        }
    }

    public bool ShowAutoBillingHint
    {
        get => _showAutoBillingHint;
        private set
        {
            if (_showAutoBillingHint == value)
            {
                return;
            }

            _showAutoBillingHint = value;
            NotifyPropertyChanged();
        }
    }

    public bool ShowDescription
    {
        get => _showDescription;
        set
        {
            if (_showDescription == value)
            {
                return;
            }

            _showDescription = value;
            NotifyPropertyChanged();
        }
    }
    public string MatterFilterSearchText
    {
        get => _matterFilterSearchText;
        set
        {
            if (_matterFilterSearchText == value)
            {
                return;
            }

            _matterFilterSearchText = value;
            NotifyPropertyChanged();
            MatterFilterView.Refresh();
            _resetMatterFilterCommand.RaiseCanExecuteChanged();
        }
    }

    public MatterFilterItem? SelectedMatterFilter
    {
        get => _selectedMatterFilter;
        set
        {
            if (_selectedMatterFilter == value)
            {
                return;
            }

            _selectedMatterFilter = value;
            NotifyPropertyChanged();
            RefreshRangeAndMatters();
            _resetMatterFilterCommand.RaiseCanExecuteChanged();
        }
    }

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

    public void RefreshEntries()
    {
        RefreshToday();
        RefreshRangeAndMatters();
    }

    private void RefreshToday()
    {
        TodayEntries.Clear();
        TodayGroups.Clear();
        var totalDuration = TimeSpan.Zero;
        var totalMinutes = 0;
        var totalRoundedMinutes = 0;
        var matterLookup = _timeEntryService.GetAllMatters().ToDictionary(matter => matter.Id);
        var entryViewModels = new List<ReportEntryViewModel>();
        foreach (var entry in _timeEntryService.GetTodayEntries())
        {
            matterLookup.TryGetValue(entry.MatterId, out var matter);
            var vm = new ReportEntryViewModel(entry, matter, _timeEntryService, HandleMatterUpdated);
            TodayEntries.Add(vm);
            entryViewModels.Add(vm);
            totalDuration += vm.Duration;
            totalMinutes += vm.ActualMinutes;
            totalRoundedMinutes += vm.RoundedMinutes;
        }

        var rvgMetricsByMatter = BuildRvgMetricsByMatter(entryViewModels, matterLookup);
        foreach (var entryViewModel in entryViewModels)
        {
            if (rvgMetricsByMatter.TryGetValue(entryViewModel.MatterId, out var metrics))
            {
                entryViewModel.SetRvgMetrics(metrics);
            }
        }

        ApplyMatterHonorarium(entryViewModels, matterLookup);
        if (entryViewModels.Count > 0)
        {
            TodayGroups.Add(new DayGroupViewModel(DateTime.Today, entryViewModels.OrderBy(vm => vm.StartLocal)));
        }
        TodayTotalDuration = totalDuration.ToString(@"hh\:mm\:ss");
        TodayTotalMinutes = totalMinutes;
        TodayTotalRoundedMinutes = totalRoundedMinutes;
    }

    private void RefreshRangeAndMatters()
    {
        var selectedMatterIds = GetSelectedMatterIds();

        RangeGroups.Clear();
        MatterGroups.Clear();
        DeleteEntries.Clear();
        _rangeEntries = new List<TimeEntry>();

        if (selectedMatterIds.Count == 0)
        {
            RangeTotalDuration = "00:00:00";
            RangeTotalMinutes = 0;
            RangeTotalRoundedMinutes = 0;
            MatterTotalDuration = "00:00:00";
            MatterTotalMinutes = 0;
            MatterTotalRoundedMinutes = 0;
            RaiseExportCanExecute();
            return;
        }

        var entries = _timeEntryService.GetEntriesInRange(FromDate, ToDate, selectedMatterIds);
        _rangeEntries = entries.ToList();
        RefreshDeleteEntries(entries);
        var matters = _timeEntryService.GetAllMatters();
        var matterLookup = matters.ToDictionary(matter => matter.Id);
        var entryViewModels = entries
            .Select(entry =>
            {
                matterLookup.TryGetValue(entry.MatterId, out var matter);
                return new ReportEntryViewModel(entry, matter, _timeEntryService, HandleMatterUpdated);
            })
            .ToList();

        var rvgMetricsByMatter = BuildRvgMetricsByMatter(entryViewModels, matterLookup);

        foreach (var entryViewModel in entryViewModels)
        {
            if (rvgMetricsByMatter.TryGetValue(entryViewModel.MatterId, out var metrics))
            {
                entryViewModel.SetRvgMetrics(metrics);
            }
        }

        ApplyMatterHonorarium(entryViewModels, matterLookup);

        var rangeGroups = entryViewModels
            .GroupBy(vm => vm.StartLocal.Date)
            .OrderBy(group => group.Key)
            .Select(group => new DayGroupViewModel(group.Key, group.OrderBy(vm => vm.StartLocal)));

        foreach (var group in rangeGroups)
        {
            RangeGroups.Add(group);
        }

        var matterGroups = entryViewModels
            .GroupBy(vm => vm.MatterId)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                matterLookup.TryGetValue(group.Key, out var matter);
                var totalMinutes = group.Sum(vm => vm.ActualMinutes);
                var metrics = matter == null ? null : CalculateRvgMetrics(matter, totalMinutes);
                return new MatterGroupViewModel(matter, group.OrderBy(vm => vm.StartLocal), metrics);
            });

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
        RaiseExportCanExecute();
    }

    private void RefreshDeleteEntries(IEnumerable<TimeEntry> entries)
    {
        DeleteEntries.Clear();
        foreach (var entry in entries.OrderByDescending(entry => entry.StartUtc))
        {
            DeleteEntries.Add(new DeleteEntryViewModel(entry));
        }
    }

    private void ApplyMatterHonorarium(
        IEnumerable<ReportEntryViewModel> entries,
        IReadOnlyDictionary<long, Matter> matterLookup)
    {
        var honorariumByMatter = entries
            .Select(vm => vm.MatterId)
            .Distinct()
            .ToDictionary(
                matterId => matterId,
                matterId =>
                {
                    matterLookup.TryGetValue(matterId, out var matter);
                    var hourlyRate = matter?.HourlyRateEurPerHour ?? 0m;
                    var totalRoundedMinutes = GetTotalRoundedMinutesForMatter(matterId);
                    var honorarStunden = ReportEntryViewModel.RoundCurrency((totalRoundedMinutes / 60m) * hourlyRate);
                    var breakdown = matter == null ? null : CalculateRvgBreakdown(matter, _rvgFeeTableService);

                    return (hourlyRate, totalRoundedMinutes, honorarStunden, breakdown);
                });

        foreach (var entry in entries)
        {
            if (honorariumByMatter.TryGetValue(entry.MatterId, out var values))
            {
                entry.SetMatterHonorarium(
                    values.hourlyRate,
                    values.totalRoundedMinutes,
                    values.honorarStunden,
                    values.breakdown);
            }
        }
    }

    private int GetTotalRoundedMinutesForMatter(long matterId)
    {
        var entries = _timeEntryService.GetEntriesForMatter(matterId);
        var totalRoundedMinutes = 0;

        foreach (var entry in entries)
        {
            var duration = TimeEntryCalculations.GetDuration(entry);
            var actualMinutes = TimeEntryCalculations.GetActualMinutes(duration);
            totalRoundedMinutes += TimeEntryCalculations.GetRoundedMinutes(actualMinutes);
        }

        return totalRoundedMinutes;
    }

    private Dictionary<long, RvgMetrics?> BuildRvgMetricsByMatter(
        IEnumerable<ReportEntryViewModel> entryViewModels,
        IReadOnlyDictionary<long, Matter> matterLookup)
    {
        return entryViewModels
            .GroupBy(vm => vm.MatterId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    if (!matterLookup.TryGetValue(group.Key, out var matter))
                    {
                        return null;
                    }

                    var totalMinutes = group.Sum(vm => vm.ActualMinutes);
                    return CalculateRvgMetrics(matter, totalMinutes);
                });
    }

    private void HandleMatterUpdated(long matterId, bool autoBillingApplied)
    {
        if (autoBillingApplied)
        {
            AutoBillingHint = "Akte wurde automatisch auf RVG-Abrechnung umgestellt.";
            ShowAutoBillingHint = true;
        }

        RefreshToday();
        RefreshRangeAndMatters();
    }

    private RvgMetrics? CalculateRvgMetrics(Matter matter, int actualMinutes)
    {
        if (matter.BillingType != BillingType.Rvg)
        {
            return null;
        }

        var breakdown = CalculateRvgBreakdown(matter, _rvgFeeTableService);
        if (breakdown == null)
        {
            return null;
        }

        var estimate = breakdown.TotalEur;
        var actualHours = actualMinutes / 60m;
        var effective = RvgCalculator.CalculateEffectiveHourlyRate(estimate, actualHours);
        var breakEven = RvgCalculator.CalculateBreakEvenTime(estimate, _timeEntryService.GetEffectiveTargetRate(matter));
        return new RvgMetrics(breakdown.Fee1_0Eur, estimate, effective, breakEven);
    }

    private static RvgBreakdown? CalculateRvgBreakdown(Matter matter, RvgFeeTableService rvgFeeTableService)
    {
        if (matter.BillingType != BillingType.Rvg)
        {
            return null;
        }

        var fee1_0 = rvgFeeTableService.LookupFee1_0(matter.SubjectValueEur);
        var businessFee = matter.BusinessFee13Enabled ? RvgCalculator.RoundCurrency(fee1_0 * 1.3m) : 0m;
        var termFee = matter.TermFee12Enabled ? RvgCalculator.RoundCurrency(fee1_0 * 1.2m) : 0m;
        var settlement10Fee = matter.SettlementFee10Enabled ? RvgCalculator.RoundCurrency(fee1_0 * 1.0m) : 0m;
        var settlement15Fee = matter.SettlementFee15Enabled ? RvgCalculator.RoundCurrency(fee1_0 * 1.5m) : 0m;
        var customFee = matter.CustomFeeFactor.HasValue
            ? RvgCalculator.RoundCurrency(fee1_0 * matter.CustomFeeFactor.Value)
            : 0m;
        var total = RvgCalculator.RoundCurrency(businessFee + termFee + settlement10Fee + settlement15Fee + customFee);

        return new RvgBreakdown(
            fee1_0,
            businessFee,
            termFee,
            settlement10Fee,
            settlement15Fee,
            customFee,
            total);
    }

    private bool CanExport() => MatterFilters.Count > 0;

    private bool FilterMatter(object item)
    {
        if (item is not MatterFilterItem matter)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(MatterFilterSearchText))
        {
            return true;
        }

        return matter.SearchText.Contains(MatterFilterSearchText, StringComparison.OrdinalIgnoreCase);
    }

    private List<long> GetSelectedMatterIds()
    {
        return SelectedMatterFilter == null
            ? MatterFilters.Select(filter => filter.Matter.Id).ToList()
            : new List<long> { SelectedMatterFilter.Matter.Id };
    }

    private void ResetMatterFilter()
    {
        SelectedMatterFilter = null;
        MatterFilterSearchText = string.Empty;
    }

    private bool CanResetMatterFilter()
    {
        return SelectedMatterFilter != null || !string.IsNullOrWhiteSpace(MatterFilterSearchText);
    }

    private void RaiseExportCanExecute()
    {
        _exportCsvCommand.RaiseCanExecuteChanged();
        _exportExcelCommand.RaiseCanExecuteChanged();
    }

    private void DeleteEntry(object? parameter)
    {
        if (parameter is not DeleteEntryViewModel entryViewModel)
        {
            return;
        }

        if (entryViewModel.Entry.EndUtc == null)
        {
            MessageBox.Show(
                "Laufende Einträge können nicht gelöscht werden. Bitte zuerst beenden.",
                "Löschen",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            "Wirklich löschen?",
            "Löschen",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            _timeEntryService.DeleteTimeEntry(entryViewModel.Entry.Id);
            RefreshEntries();
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "Löschen", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CreateBillingDraftsFromView()
    {
        CreateBillingDrafts(_rangeEntries);
    }

    private void CreateBillingDraftsFromAllOpen()
    {
        var entries = _databaseService.GetUnbilledEntries(onlyCompleted: true);
        CreateBillingDrafts(entries);
    }

    private void CreateBillingDrafts(IEnumerable<TimeEntry> entries)
    {
        var entryList = entries?.ToList() ?? new List<TimeEntry>();
        var entryIds = entryList.Select(entry => entry.Id).Distinct().ToList();

        if (entryIds.Count == 0)
        {
            MessageBox.Show("Keine Einträge", "Abrechnung", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var billedEntryIds = entryList
            .Where(entry => entry.Billed)
            .Select(entry => entry.Id)
            .Distinct()
            .ToList();

        if (billedEntryIds.Count > 0)
        {
            var result = MessageBox.Show(
                "Es sind bereits abgerechnete Einträge enthalten. Ausschließen?",
                "Abrechnung",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.Yes);

            if (result == MessageBoxResult.Yes)
            {
                entryIds = entryIds.Except(billedEntryIds).ToList();
                if (entryIds.Count == 0)
                {
                    MessageBox.Show("Keine Einträge", "Abrechnung", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }
        }

        var batch = _billingService.CreateBillingBatchDraft(entryIds);
        var wizard = new BillingWizardWindow(batch.BatchId);
        wizard.Show();
        wizard.Activate();
    }

    private void ExportCsv()
    {
        var dialog = new SaveFileDialog
        {
            Title = "CSV exportieren",
            Filter = "CSV-Datei (*.csv)|*.csv",
            FileName = $"AkteTimer_Export_{FromDate:yyyyMMdd}-{ToDate:yyyyMMdd}.csv"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var exportRows = BuildExportRows();
        var builder = new StringBuilder();
        WriteCsvRow(
            builder,
            "Start",
            "Ende",
            "Akte",
            "Hashtag",
            "Dauer",
            "6-Minuten",
            "Stundensatz",
            "Zeit-Honorar",
            "RVG-Honorar",
            "Streitwert",
            "Gebühr (wahl)",
            "Geschäft 1,3",
            "Termin 1,2",
            "Vergleich 1,0",
            "Vergleich 1,5",
            "Effektivität",
            "Beschreibung");

        foreach (var row in exportRows)
        {
            WriteCsvRow(
                builder,
                row.Start.ToString("dd.MM.yyyy HH:mm:ss"),
                row.End.ToString("dd.MM.yyyy HH:mm:ss"),
                row.Matter,
                row.Hashtag,
                row.Duration.ToString(@"hh\:mm\:ss"),
                row.RoundedMinutes.ToString(),
                row.HourlyRate.ToString("N2"),
                row.HonorarStundenMatter.ToString("N2"),
                row.HonorarRvgMatter.ToString("N2"),
                row.SubjectValueEur.ToString("N2"),
                FormatFeeFactor(row.CustomFeeFactor),
                FormatToggle(row.BusinessFee13Enabled),
                FormatToggle(row.TermFee12Enabled),
                FormatToggle(row.SettlementFee10Enabled),
                FormatToggle(row.SettlementFee15Enabled),
                row.EffektivitätMatter.ToString("N2"),
                row.Note);
        }

        try
        {
            File.WriteAllText(dialog.FileName, builder.ToString(), new UTF8Encoding(true));
            MessageBox.Show("CSV-Export wurde erstellt.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"CSV-Export fehlgeschlagen: {ex.Message}", "Export", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportExcel()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Excel exportieren",
            Filter = "Excel-Datei (*.xlsx)|*.xlsx",
            FileName = $"AkteTimer_Export_{FromDate:yyyyMMdd}-{ToDate:yyyyMMdd}.xlsx"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var exportRows = BuildExportRows();

        try
        {
            using var workbook = new XLWorkbook();
            WriteEntriesSheet(workbook, exportRows);
            WriteSummariesByMatterSheet(workbook, exportRows);
            WriteSummariesByDateSheet(workbook, exportRows);
            workbook.SaveAs(dialog.FileName);
            MessageBox.Show("Excel-Export wurde erstellt.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Excel-Export fehlgeschlagen: {ex.Message}", "Export", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private List<ExportRow> BuildExportRows()
    {
        var selectedMatterIds = GetSelectedMatterIds();

        if (selectedMatterIds.Count == 0)
        {
            return new List<ExportRow>();
        }

        var entries = _timeEntryService.GetEntriesInRange(FromDate, ToDate, selectedMatterIds);
        var matters = _timeEntryService.GetAllMatters();
        var matterLookup = matters.ToDictionary(matter => matter.Id);
        var entryViewModels = entries
            .Select(entry =>
            {
                matterLookup.TryGetValue(entry.MatterId, out var matter);
                return new ReportEntryViewModel(entry, matter, _timeEntryService, HandleMatterUpdated);
            })
            .ToList();

        var rvgMetricsByMatter = BuildRvgMetricsByMatter(entryViewModels, matterLookup);
        foreach (var entryViewModel in entryViewModels)
        {
            if (rvgMetricsByMatter.TryGetValue(entryViewModel.MatterId, out var metrics))
            {
                entryViewModel.SetRvgMetrics(metrics);
            }
        }

        ApplyMatterHonorarium(entryViewModels, matterLookup);

        return entryViewModels
            .Select(CreateExportRow)
            .OrderBy(row => row.Start)
            .ToList();
    }

    private static ExportRow CreateExportRow(ReportEntryViewModel entryViewModel)
    {
        return new ExportRow(
            entryViewModel.StartLocal.Date,
            entryViewModel.StartLocal,
            entryViewModel.EndLocal,
            entryViewModel.Duration,
            entryViewModel.RoundedMinutes,
            entryViewModel.Matter,
            entryViewModel.Hashtag,
            entryViewModel.HourlyRate,
            entryViewModel.HonorarStundenMatter,
            entryViewModel.HonorarRvgMatter,
            entryViewModel.SubjectValueEur,
            entryViewModel.CustomFeeFactor,
            entryViewModel.BusinessFee13Enabled,
            entryViewModel.TermFee12Enabled,
            entryViewModel.SettlementFee10Enabled,
            entryViewModel.SettlementFee15Enabled,
            entryViewModel.EffektivitätMatter,
            entryViewModel.Note);
    }

    private static void WriteCsvRow(StringBuilder builder, params string[] values)
    {
        var escaped = values.Select(EscapeCsvValue);
        builder.AppendLine(string.Join(",", escaped));
    }

    private static string EscapeCsvValue(string value)
    {
        if (value.Contains('"'))
        {
            value = value.Replace("\"", "\"\"");
        }

        if (value.Contains(',') || value.Contains('\n') || value.Contains('\r') || value.Contains('"'))
        {
            return $"\"{value}\"";
        }

        return value;
    }

    private static void WriteEntriesSheet(XLWorkbook workbook, IReadOnlyList<ExportRow> rows)
    {
        var sheet = workbook.Worksheets.Add("Eintraege");
        WriteEntriesHeader(sheet);

        var rowIndex = 2;
        foreach (var row in rows)
        {
            sheet.Cell(rowIndex, 1).Value = row.Start.ToString("dd.MM.yyyy HH:mm:ss");
            sheet.Cell(rowIndex, 2).Value = row.End.ToString("dd.MM.yyyy HH:mm:ss");
            sheet.Cell(rowIndex, 3).Value = row.Matter;
            sheet.Cell(rowIndex, 4).Value = row.Hashtag;
            sheet.Cell(rowIndex, 5).Value = row.Duration.ToString(@"hh\:mm\:ss");
            sheet.Cell(rowIndex, 6).Value = row.RoundedMinutes;
            sheet.Cell(rowIndex, 7).Value = row.HourlyRate;
            sheet.Cell(rowIndex, 8).Value = row.HonorarStundenMatter;
            sheet.Cell(rowIndex, 9).Value = row.HonorarRvgMatter;
            sheet.Cell(rowIndex, 10).Value = row.SubjectValueEur;
            sheet.Cell(rowIndex, 11).Value = row.CustomFeeFactor.HasValue ? (double)row.CustomFeeFactor.Value : string.Empty;
            sheet.Cell(rowIndex, 12).Value = FormatToggle(row.BusinessFee13Enabled);
            sheet.Cell(rowIndex, 13).Value = FormatToggle(row.TermFee12Enabled);
            sheet.Cell(rowIndex, 14).Value = FormatToggle(row.SettlementFee10Enabled);
            sheet.Cell(rowIndex, 15).Value = FormatToggle(row.SettlementFee15Enabled);
            sheet.Cell(rowIndex, 16).Value = row.EffektivitätMatter;
            sheet.Cell(rowIndex, 17).Value = row.Note;
            rowIndex++;
        }

        sheet.Column(6).Style.NumberFormat.Format = "0";
        sheet.Column(7).Style.NumberFormat.Format = "#,##0.00";
        sheet.Column(8).Style.NumberFormat.Format = "#,##0.00";
        sheet.Column(9).Style.NumberFormat.Format = "#,##0.00";
        sheet.Column(10).Style.NumberFormat.Format = "#,##0.00";
        sheet.Column(11).Style.NumberFormat.Format = "0.0";
        sheet.Column(16).Style.NumberFormat.Format = "#,##0.00";
        sheet.Columns().AdjustToContents();
    }

    private static void WriteEntriesHeader(IXLWorksheet sheet)
    {
        sheet.Cell(1, 1).Value = "Start";
        sheet.Cell(1, 2).Value = "Ende";
        sheet.Cell(1, 3).Value = "Akte";
        sheet.Cell(1, 4).Value = "Hashtag";
        sheet.Cell(1, 5).Value = "Dauer";
        sheet.Cell(1, 6).Value = "6-Minuten";
        sheet.Cell(1, 7).Value = "Stundensatz";
        sheet.Cell(1, 8).Value = "Zeit-Honorar";
        sheet.Cell(1, 9).Value = "RVG-Honorar";
        sheet.Cell(1, 10).Value = "Streitwert";
        sheet.Cell(1, 11).Value = "Gebühr (wahl)";
        sheet.Cell(1, 12).Value = "Geschäft 1,3";
        sheet.Cell(1, 13).Value = "Termin 1,2";
        sheet.Cell(1, 14).Value = "Vergleich 1,0";
        sheet.Cell(1, 15).Value = "Vergleich 1,5";
        sheet.Cell(1, 16).Value = "Effektivität";
        sheet.Cell(1, 17).Value = "Beschreibung";
    }

    private static string FormatToggle(bool enabled) => enabled ? "on" : "off";

    private static string FormatFeeFactor(decimal? feeFactor)
    {
        return feeFactor.HasValue ? feeFactor.Value.ToString("F1") : string.Empty;
    }

    private static void WriteSummariesByMatterSheet(XLWorkbook workbook, IReadOnlyList<ExportRow> rows)
    {
        var sheet = workbook.Worksheets.Add("Summen_Pro_Akte");
        sheet.Cell(1, 1).Value = "Akte";
        sheet.Cell(1, 2).Value = "6-Minuten";

        var rowIndex = 2;
        var groups = rows
            .GroupBy(row => row.Matter)
            .OrderBy(group => group.Key);

        foreach (var group in groups)
        {
            sheet.Cell(rowIndex, 1).Value = group.Key;
            sheet.Cell(rowIndex, 2).Value = group.Sum(entry => entry.RoundedMinutes);
            rowIndex++;
        }

        sheet.Columns().AdjustToContents();
    }

    private static void WriteSummariesByDateSheet(XLWorkbook workbook, IReadOnlyList<ExportRow> rows)
    {
        var sheet = workbook.Worksheets.Add("Summen_Pro_Tag");
        sheet.Cell(1, 1).Value = "Datum";
        sheet.Cell(1, 2).Value = "6-Minuten";

        var rowIndex = 2;
        var groups = rows
            .GroupBy(row => row.Date)
            .OrderBy(group => group.Key);

        foreach (var group in groups)
        {
            sheet.Cell(rowIndex, 1).Value = group.Key.ToString("dd.MM.yyyy");
            sheet.Cell(rowIndex, 2).Value = group.Sum(entry => entry.RoundedMinutes);
            rowIndex++;
        }

        sheet.Columns().AdjustToContents();
    }
}

public sealed record ExportRow(
    DateTime Date,
    DateTime Start,
    DateTime End,
    TimeSpan Duration,
    int RoundedMinutes,
    string Matter,
    string Hashtag,
    decimal HourlyRate,
    decimal HonorarStundenMatter,
    decimal HonorarRvgMatter,
    decimal SubjectValueEur,
    decimal? CustomFeeFactor,
    bool BusinessFee13Enabled,
    bool TermFee12Enabled,
    bool SettlementFee10Enabled,
    bool SettlementFee15Enabled,
    decimal EffektivitätMatter,
    string Note);

public sealed record RvgBreakdown(
    decimal Fee1_0Eur,
    decimal BusinessFee13Eur,
    decimal TermFee12Eur,
    decimal SettlementFee10Eur,
    decimal SettlementFee15Eur,
    decimal CustomFeeEur,
    decimal TotalEur);

public sealed class MatterFilterItem : ViewModelBase
{
    public MatterFilterItem(Matter matter)
    {
        Matter = matter;
    }

    public Matter Matter { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(Matter.Title)
        ? Matter.FileRef
        : $"{Matter.FileRef} – {Matter.Title}";

    public string SearchText => string.IsNullOrWhiteSpace(Matter.Title)
        ? Matter.FileRef
        : $"{Matter.FileRef} {Matter.Title}";
}

public sealed class ReportEntryViewModel : ViewModelBase
{
    private readonly TimeEntryService _timeEntryService;
    private readonly Matter? _matter;
    private readonly Action<long, bool>? _matterUpdated;
    private BillingType _billingType;
    private decimal _hourlyRate;
    private decimal _subjectValueEur;
    private decimal? _customFeeFactor;
    private bool _businessFee13Enabled;
    private bool _termFee12Enabled;
    private bool _settlementFee10Enabled;
    private bool _settlementFee15Enabled;
    private decimal _businessFee13Eur;
    private decimal _termFee12Eur;
    private decimal _settlementFee10Eur;
    private decimal _settlementFee15Eur;
    private decimal _customFeeEur;
    private string _note = string.Empty;

    public ReportEntryViewModel(TimeEntry entry, Matter? matter, TimeEntryService timeEntryService, Action<long, bool> matterUpdated)
    {
        _timeEntryService = timeEntryService;
        _matter = matter;
        _matterUpdated = matterUpdated;
        Entry = entry;
        MatterId = entry.MatterId;
        Matter = entry.MatterFileRef ?? "-";
        Hashtag = entry.Hashtag ?? string.Empty;
        StartLocal = entry.StartUtc.ToLocalTime();
        EndLocal = (entry.EndUtc ?? DateTime.UtcNow).ToLocalTime();
        Duration = TimeEntryCalculations.GetDuration(entry);
        DurationText = Duration.ToString(@"hh\:mm\:ss");
        ActualMinutes = TimeEntryCalculations.GetActualMinutes(Duration);
        RoundedMinutes = TimeEntryCalculations.GetRoundedMinutes(ActualMinutes);
        _note = entry.Note ?? string.Empty;
        _billingType = matter?.BillingType ?? BillingType.Hourly;
        _hourlyRate = matter?.HourlyRateEurPerHour ?? 0m;
        _subjectValueEur = matter?.SubjectValueEur ?? 0m;
        _customFeeFactor = matter?.CustomFeeFactor;
        _businessFee13Enabled = matter?.BusinessFee13Enabled ?? false;
        _termFee12Enabled = matter?.TermFee12Enabled ?? false;
        _settlementFee10Enabled = matter?.SettlementFee10Enabled ?? false;
        _settlementFee15Enabled = matter?.SettlementFee15Enabled ?? false;
        SetMatterHonorarium(matter?.HourlyRateEurPerHour ?? 0m, 0, 0m, null);
    }

    public TimeEntry Entry { get; }
    public long MatterId { get; }
    public string Matter { get; }
    public string Hashtag { get; }
    public DateTime StartLocal { get; }
    public DateTime EndLocal { get; }
    public TimeSpan Duration { get; }
    public string DurationText { get; }
    public int ActualMinutes { get; }
    public int RoundedMinutes { get; }
    public string Note
    {
        get => _note;
        set
        {
            var next = value ?? string.Empty;
            if (_note == next)
            {
                return;
            }

            _note = next;
            Entry.Note = string.IsNullOrWhiteSpace(next) ? null : next;
            NotifyPropertyChanged();
            _timeEntryService.UpdateTimeEntryNote(Entry.Id, Entry.Note);
        }
    }
    public BillingType BillingType
    {
        get => _billingType;
        private set
        {
            if (_billingType == value)
            {
                return;
            }

            _billingType = value;
            NotifyPropertyChanged();
        }
    }
    public decimal EinzelHonorar { get; private set; }
    public decimal HonorarStundenMatter { get; private set; }
    public decimal HonorarRvgMatter { get; private set; }
    public decimal EffektivitätMatter { get; private set; }
    public string RvgFormulaTooltip { get; private set; } = "-";
    public string RvgEstimateText { get; private set; } = "-";
    public string EffectiveHourlyRateText { get; private set; } = "-";
    public string BreakEvenTimeText { get; private set; } = "-";
    public decimal BusinessFee13Eur => _businessFee13Eur;
    public decimal TermFee12Eur => _termFee12Eur;
    public decimal SettlementFee10Eur => _settlementFee10Eur;
    public decimal SettlementFee15Eur => _settlementFee15Eur;
    public decimal CustomFeeEur => _customFeeEur;
    public string BusinessFee13EurText => FormatRvgComponent(_businessFee13Enabled, _businessFee13Eur);
    public string TermFee12EurText => FormatRvgComponent(_termFee12Enabled, _termFee12Eur);
    public string SettlementFee10EurText => FormatRvgComponent(_settlementFee10Enabled, _settlementFee10Eur);
    public string SettlementFee15EurText => FormatRvgComponent(_settlementFee15Enabled, _settlementFee15Eur);
    public string CustomFeeEurText => _customFeeFactor.HasValue ? FormatCurrency(_customFeeEur) : string.Empty;

    public decimal HourlyRate
    {
        get => _hourlyRate;
        set
        {
            var next = Math.Max(0m, value);
            if (_hourlyRate == next)
            {
                return;
            }

            _hourlyRate = next;
            NotifyPropertyChanged();
            UpdateMatter(matter => matter.HourlyRateEurPerHour = next);
        }
    }

    public decimal SubjectValueEur
    {
        get => _subjectValueEur;
        set
        {
            var next = Math.Max(0m, value);
            if (_subjectValueEur == next)
            {
                return;
            }

            _subjectValueEur = next;
            NotifyPropertyChanged();
            UpdateMatter(matter => matter.SubjectValueEur = next, recomputeBillingType: true);
        }
    }

    public decimal? CustomFeeFactor
    {
        get => _customFeeFactor;
        set
        {
            var normalized = NormalizeCustomFeeFactor(value);
            if (_customFeeFactor == normalized)
            {
                return;
            }

            _customFeeFactor = normalized;
            NotifyPropertyChanged();
            NotifyPropertyChanged(nameof(CustomFeeEurText));
            UpdateMatter(matter => matter.CustomFeeFactor = normalized, recomputeBillingType: true);
        }
    }

    public bool BusinessFee13Enabled
    {
        get => _businessFee13Enabled;
        set
        {
            if (_businessFee13Enabled == value)
            {
                return;
            }

            _businessFee13Enabled = value;
            NotifyPropertyChanged();
            UpdateMatter(matter => matter.BusinessFee13Enabled = value, recomputeBillingType: true);
        }
    }

    public bool TermFee12Enabled
    {
        get => _termFee12Enabled;
        set
        {
            if (_termFee12Enabled == value)
            {
                return;
            }

            _termFee12Enabled = value;
            NotifyPropertyChanged();
            UpdateMatter(matter => matter.TermFee12Enabled = value, recomputeBillingType: true);
        }
    }

    public bool SettlementFee10Enabled
    {
        get => _settlementFee10Enabled;
        set
        {
            if (_settlementFee10Enabled == value)
            {
                return;
            }

            _settlementFee10Enabled = value;
            NotifyPropertyChanged();
            UpdateMatter(matter => matter.SettlementFee10Enabled = value, recomputeBillingType: true);
        }
    }

    public bool SettlementFee15Enabled
    {
        get => _settlementFee15Enabled;
        set
        {
            if (_settlementFee15Enabled == value)
            {
                return;
            }

            _settlementFee15Enabled = value;
            NotifyPropertyChanged();
            UpdateMatter(matter => matter.SettlementFee15Enabled = value, recomputeBillingType: true);
        }
    }

    public void SetMatterHonorarium(
        decimal hourlyRateEurPerHour,
        int sumRoundedMinutesMatter,
        decimal honorarStundenMatter,
        RvgBreakdown? breakdown)
    {
        _hourlyRate = hourlyRateEurPerHour;
        EinzelHonorar = RoundCurrency((RoundedMinutes / 60m) * hourlyRateEurPerHour);
        HonorarStundenMatter = honorarStundenMatter;
        HonorarRvgMatter = breakdown?.TotalEur ?? 0m;
        EffektivitätMatter = breakdown == null ? 0m : RoundCurrency(breakdown.TotalEur - honorarStundenMatter);
        RvgFormulaTooltip = breakdown == null
            ? "-"
            : $"1,0-Gebühr: {breakdown.Fee1_0Eur:N2} €, GB 1,3: {breakdown.BusinessFee13Eur:N2} €, " +
              $"Termin 1,2: {breakdown.TermFee12Eur:N2} €, Vergleich 1,0: {breakdown.SettlementFee10Eur:N2} €, " +
              $"Vergleich 1,5: {breakdown.SettlementFee15Eur:N2} €, Wahl: {breakdown.CustomFeeEur:N2} €";
        _businessFee13Eur = breakdown?.BusinessFee13Eur ?? 0m;
        _termFee12Eur = breakdown?.TermFee12Eur ?? 0m;
        _settlementFee10Eur = breakdown?.SettlementFee10Eur ?? 0m;
        _settlementFee15Eur = breakdown?.SettlementFee15Eur ?? 0m;
        _customFeeEur = breakdown?.CustomFeeEur ?? 0m;
        NotifyPropertyChanged(nameof(HourlyRate));
        NotifyPropertyChanged(nameof(EinzelHonorar));
        NotifyPropertyChanged(nameof(HonorarStundenMatter));
        NotifyPropertyChanged(nameof(HonorarRvgMatter));
        NotifyPropertyChanged(nameof(EffektivitätMatter));
        NotifyPropertyChanged(nameof(RvgFormulaTooltip));
        NotifyPropertyChanged(nameof(BusinessFee13Eur));
        NotifyPropertyChanged(nameof(TermFee12Eur));
        NotifyPropertyChanged(nameof(SettlementFee10Eur));
        NotifyPropertyChanged(nameof(SettlementFee15Eur));
        NotifyPropertyChanged(nameof(CustomFeeEur));
        NotifyPropertyChanged(nameof(BusinessFee13EurText));
        NotifyPropertyChanged(nameof(TermFee12EurText));
        NotifyPropertyChanged(nameof(SettlementFee10EurText));
        NotifyPropertyChanged(nameof(SettlementFee15EurText));
        NotifyPropertyChanged(nameof(CustomFeeEurText));
    }

    public void SetRvgMetrics(RvgMetrics? metrics)
    {
        if (metrics == null)
        {
            RvgEstimateText = "-";
            EffectiveHourlyRateText = "-";
            BreakEvenTimeText = "-";
            NotifyPropertyChanged(nameof(RvgEstimateText));
            NotifyPropertyChanged(nameof(EffectiveHourlyRateText));
            NotifyPropertyChanged(nameof(BreakEvenTimeText));
            return;
        }

        RvgEstimateText = metrics.EstimateEur.ToString("N2");
        EffectiveHourlyRateText = metrics.EffectiveHourlyRateEur?.ToString("N2") ?? "-";
        BreakEvenTimeText = metrics.BreakEvenTime == null ? "-" : RvgCalculator.FormatBreakEvenTime(metrics.BreakEvenTime.Value);
        NotifyPropertyChanged(nameof(RvgEstimateText));
        NotifyPropertyChanged(nameof(EffectiveHourlyRateText));
        NotifyPropertyChanged(nameof(BreakEvenTimeText));
    }

    public static decimal RoundCurrency(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal? NormalizeCustomFeeFactor(decimal? value)
    {
        if (value == null)
        {
            return null;
        }

        var clamped = Math.Clamp(value.Value, 0.1m, 3m);
        return Math.Round(clamped, 1, MidpointRounding.AwayFromZero);
    }

    private static string FormatRvgComponent(bool enabled, decimal amount)
    {
        return enabled ? FormatCurrency(amount) : "—";
    }

    private static string FormatCurrency(decimal amount)
    {
        return $"{amount:N2} €";
    }

    private void UpdateMatter(Action<Matter> updateAction, bool recomputeBillingType = false)
    {
        if (_matter == null)
        {
            return;
        }

        updateAction(_matter);
        var autoBillingApplied = false;
        if (recomputeBillingType)
        {
            var nextBillingType = BillingTypeRules.RecomputeBillingType(_matter);
            if (_matter.BillingType != nextBillingType)
            {
                _matter.BillingType = nextBillingType;
                BillingType = nextBillingType;
                autoBillingApplied = nextBillingType == BillingType.Rvg;
            }
        }
        _timeEntryService.UpdateMatter(_matter);
        _matterUpdated?.Invoke(_matter.Id, autoBillingApplied);
    }
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
        TotalHonorarEur = ReportEntryViewModel.RoundCurrency(Entries.Sum(vm => vm.EinzelHonorar));
    }

    public DateTime Date { get; }
    public string DateText => Date.ToString("dd.MM.yyyy");
    public string DateLongText => Date.ToString("dddd, d. MMMM yyyy");
    public ObservableCollection<ReportEntryViewModel> Entries { get; }
    public string TotalDurationText { get; }
    public int TotalActualMinutes { get; }
    public int TotalRoundedMinutes { get; }
    public decimal TotalHonorarEur { get; }
}

public sealed class MatterGroupViewModel
{
    public MatterGroupViewModel(Matter? matter, IEnumerable<ReportEntryViewModel> entries, RvgMetrics? metrics)
    {
        Matter = matter?.FileRef ?? "-";
        Entries = new ObservableCollection<ReportEntryViewModel>(entries);
        var totalDuration = Entries.Aggregate(TimeSpan.Zero, (current, vm) => current + vm.Duration);
        TotalDurationText = totalDuration.ToString(@"hh\:mm\:ss");
        TotalActualMinutes = Entries.Sum(vm => vm.ActualMinutes);
        TotalRoundedMinutes = Entries.Sum(vm => vm.RoundedMinutes);
        ShowRvgMetrics = metrics != null;
        RvgEstimateText = metrics?.EstimateEur.ToString("N2") ?? "-";
        EffectiveHourlyRateText = metrics?.EffectiveHourlyRateEur?.ToString("N2") ?? "-";
        BreakEvenTimeText = metrics?.BreakEvenTime == null ? "-" : RvgCalculator.FormatBreakEvenTime(metrics.BreakEvenTime.Value);
    }

    public string Matter { get; }
    public ObservableCollection<ReportEntryViewModel> Entries { get; }
    public string TotalDurationText { get; }
    public int TotalActualMinutes { get; }
    public int TotalRoundedMinutes { get; }
    public bool ShowRvgMetrics { get; }
    public string RvgEstimateText { get; }
    public string EffectiveHourlyRateText { get; }
    public string BreakEvenTimeText { get; }
}

public sealed class DeleteEntryViewModel
{
    public DeleteEntryViewModel(TimeEntry entry)
    {
        Entry = entry;
        var startLocal = entry.StartUtc.ToLocalTime();
        DateLocal = startLocal.Date;
        StartLocal = startLocal;
        EndText = entry.EndUtc == null ? "läuft" : entry.EndUtc.Value.ToLocalTime().ToString("HH:mm:ss");
        Matter = entry.MatterFileRef ?? "-";
        Hashtag = entry.Hashtag ?? string.Empty;
        Note = entry.Note ?? string.Empty;
        DurationText = TimeEntryCalculations.GetDuration(entry).ToString(@"hh\:mm\:ss");
    }

    public TimeEntry Entry { get; }
    public DateTime DateLocal { get; }
    public DateTime StartLocal { get; }
    public string EndText { get; }
    public string Matter { get; }
    public string Hashtag { get; }
    public string DurationText { get; }
    public string Note { get; }
}
