using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using AkteTimer.Models;
using AkteTimer.Services;
using ClosedXML.Excel;
using Microsoft.Win32;
using MessageBox = System.Windows.MessageBox;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace AkteTimer.ViewModels;

public sealed class ReportsViewModel : ViewModelBase
{
    private readonly TimeEntryService _timeEntryService;
    private readonly RvgFeeTableService _rvgFeeTableService = new();
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
    private readonly RelayCommand _exportCsvCommand;
    private readonly RelayCommand _exportExcelCommand;
    private MatterDetailsViewModel? _selectedMatterDetails;

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
            MatterDetails.Add(new MatterDetailsViewModel(matter, _timeEntryService, _rvgFeeTableService));
        }

        _exportCsvCommand = new RelayCommand(_ => ExportCsv(), _ => CanExport());
        _exportExcelCommand = new RelayCommand(_ => ExportExcel(), _ => CanExport());

        SelectedMatterDetails = MatterDetails.FirstOrDefault();
        RefreshToday();
        RefreshRangeAndMatters();
    }

    public ObservableCollection<ReportEntryViewModel> TodayEntries { get; } = new();

    public ObservableCollection<MatterFilterItem> MatterFilters { get; } = new();

    public ObservableCollection<DayGroupViewModel> RangeGroups { get; } = new();

    public ObservableCollection<MatterGroupViewModel> MatterGroups { get; } = new();

    public ObservableCollection<MatterDetailsViewModel> MatterDetails { get; } = new();

    public RelayCommand ExportCsvCommand => _exportCsvCommand;

    public RelayCommand ExportExcelCommand => _exportExcelCommand;

    public IReadOnlyList<BillingType> BillingTypeOptions { get; } = Enum.GetValues<BillingType>();

    public MatterDetailsViewModel? SelectedMatterDetails
    {
        get => _selectedMatterDetails;
        set
        {
            if (_selectedMatterDetails == value)
            {
                return;
            }

            _selectedMatterDetails = value;
            NotifyPropertyChanged();
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
        var matterLookup = _timeEntryService.GetAllMatters().ToDictionary(matter => matter.Id);
        foreach (var entry in _timeEntryService.GetTodayEntries())
        {
            matterLookup.TryGetValue(entry.MatterId, out var matter);
            var vm = new ReportEntryViewModel(entry, matter);
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
            RaiseExportCanExecute();
            return;
        }

        var entries = _timeEntryService.GetEntriesInRange(FromDate, ToDate, selectedMatterIds);
        var matters = _timeEntryService.GetAllMatters();
        var matterLookup = matters.ToDictionary(matter => matter.Id);
        var entryViewModels = entries
            .Select(entry =>
            {
                matterLookup.TryGetValue(entry.MatterId, out var matter);
                return new ReportEntryViewModel(entry, matter);
            })
            .ToList();

        var rvgMetricsByMatter = entryViewModels
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

        foreach (var entryViewModel in entryViewModels)
        {
            if (rvgMetricsByMatter.TryGetValue(entryViewModel.MatterId, out var metrics))
            {
                entryViewModel.SetRvgMetrics(metrics);
            }
        }

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

    private RvgMetrics? CalculateRvgMetrics(Matter matter, int actualMinutes)
    {
        if (matter.BillingType != BillingType.Rvg)
        {
            return null;
        }

        var fee1_0 = _rvgFeeTableService.LookupFee1_0(matter.SubjectValueEur);
        var estimate = RvgCalculator.CalculateEstimate(fee1_0, matter.FeeFactor);
        var actualHours = actualMinutes / 60m;
        var effective = RvgCalculator.CalculateEffectiveHourlyRate(estimate, actualHours);
        var breakEven = RvgCalculator.CalculateBreakEvenTime(estimate, matter.TargetRateEurPerHour);
        return new RvgMetrics(fee1_0, estimate, effective, breakEven);
    }

    private bool CanExport() => MatterFilters.Any(filter => filter.IsSelected);

    private void RaiseExportCanExecute()
    {
        _exportCsvCommand.RaiseCanExecuteChanged();
        _exportExcelCommand.RaiseCanExecuteChanged();
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
        WriteCsvRow(builder, "Datum", "Start", "Ende", "IstDauer_hhmmss", "IstMinuten", "Abrechnung6Minuten", "Aktenzeichen", "Hashtag", "Notiz");

        foreach (var row in exportRows)
        {
            WriteCsvRow(
                builder,
                row.Date.ToString("dd.MM.yyyy"),
                row.Start.ToString("HH:mm:ss"),
                row.End.ToString("HH:mm:ss"),
                row.Duration.ToString(@"hh\:mm\:ss"),
                row.ActualMinutes.ToString(),
                row.RoundedMinutes.ToString(),
                row.Matter,
                row.Hashtag,
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
        var selectedMatterIds = MatterFilters
            .Where(filter => filter.IsSelected)
            .Select(filter => filter.Matter.Id)
            .ToList();

        if (selectedMatterIds.Count == 0)
        {
            return new List<ExportRow>();
        }

        var nowUtc = DateTime.UtcNow;
        var entries = _timeEntryService.GetEntriesInRange(FromDate, ToDate, selectedMatterIds);
        return entries
            .Select(entry => CreateExportRow(entry, nowUtc))
            .OrderBy(row => row.Start)
            .ToList();
    }

    private static ExportRow CreateExportRow(TimeEntry entry, DateTime nowUtc)
    {
        var startLocal = entry.StartUtc.ToLocalTime();
        var endLocal = (entry.EndUtc ?? nowUtc).ToLocalTime();
        var duration = TimeEntryCalculations.GetDuration(entry, nowUtc);
        var actualMinutes = TimeEntryCalculations.GetActualMinutes(duration);
        var roundedMinutes = TimeEntryCalculations.GetRoundedMinutes(actualMinutes);
        var note = entry.Note?.Trim() ?? string.Empty;
        var hashtag = entry.Hashtag?.Trim() ?? string.Empty;

        return new ExportRow(
            startLocal.Date,
            startLocal,
            endLocal,
            duration,
            actualMinutes,
            roundedMinutes,
            entry.MatterFileRef ?? "-",
            hashtag,
            note);
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
            sheet.Cell(rowIndex, 1).Value = row.Date.ToString("dd.MM.yyyy");
            sheet.Cell(rowIndex, 2).Value = row.Start.ToString("HH:mm:ss");
            sheet.Cell(rowIndex, 3).Value = row.End.ToString("HH:mm:ss");
            sheet.Cell(rowIndex, 4).Value = row.Duration.ToString(@"hh\:mm\:ss");
            sheet.Cell(rowIndex, 5).Value = row.ActualMinutes;
            sheet.Cell(rowIndex, 6).Value = row.RoundedMinutes;
            sheet.Cell(rowIndex, 7).Value = row.Matter;
            sheet.Cell(rowIndex, 8).Value = row.Hashtag;
            sheet.Cell(rowIndex, 9).Value = row.Note;
            rowIndex++;
        }

        sheet.Columns().AdjustToContents();
    }

    private static void WriteEntriesHeader(IXLWorksheet sheet)
    {
        sheet.Cell(1, 1).Value = "Datum";
        sheet.Cell(1, 2).Value = "Start";
        sheet.Cell(1, 3).Value = "Ende";
        sheet.Cell(1, 4).Value = "IstDauer_hhmmss";
        sheet.Cell(1, 5).Value = "IstMinuten";
        sheet.Cell(1, 6).Value = "Abrechnung6Minuten";
        sheet.Cell(1, 7).Value = "Aktenzeichen";
        sheet.Cell(1, 8).Value = "Hashtag";
        sheet.Cell(1, 9).Value = "Notiz";
    }

    private static void WriteSummariesByMatterSheet(XLWorkbook workbook, IReadOnlyList<ExportRow> rows)
    {
        var sheet = workbook.Worksheets.Add("Summen_Pro_Akte");
        sheet.Cell(1, 1).Value = "Akte";
        sheet.Cell(1, 2).Value = "IstMinuten";
        sheet.Cell(1, 3).Value = "Abrechnung6Minuten";

        var rowIndex = 2;
        var groups = rows
            .GroupBy(row => row.Matter)
            .OrderBy(group => group.Key);

        foreach (var group in groups)
        {
            sheet.Cell(rowIndex, 1).Value = group.Key;
            sheet.Cell(rowIndex, 2).Value = group.Sum(entry => entry.ActualMinutes);
            sheet.Cell(rowIndex, 3).Value = group.Sum(entry => entry.RoundedMinutes);
            rowIndex++;
        }

        sheet.Columns().AdjustToContents();
    }

    private static void WriteSummariesByDateSheet(XLWorkbook workbook, IReadOnlyList<ExportRow> rows)
    {
        var sheet = workbook.Worksheets.Add("Summen_Pro_Tag");
        sheet.Cell(1, 1).Value = "Datum";
        sheet.Cell(1, 2).Value = "IstMinuten";
        sheet.Cell(1, 3).Value = "Abrechnung6Minuten";

        var rowIndex = 2;
        var groups = rows
            .GroupBy(row => row.Date)
            .OrderBy(group => group.Key);

        foreach (var group in groups)
        {
            sheet.Cell(rowIndex, 1).Value = group.Key.ToString("dd.MM.yyyy");
            sheet.Cell(rowIndex, 2).Value = group.Sum(entry => entry.ActualMinutes);
            sheet.Cell(rowIndex, 3).Value = group.Sum(entry => entry.RoundedMinutes);
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
    int ActualMinutes,
    int RoundedMinutes,
    string Matter,
    string Hashtag,
    string Note);

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
    public ReportEntryViewModel(TimeEntry entry, Matter? matter)
    {
        MatterId = entry.MatterId;
        Matter = entry.MatterFileRef ?? "-";
        Hashtag = entry.Hashtag ?? string.Empty;
        StartLocal = entry.StartUtc.ToLocalTime();
        EndLocal = (entry.EndUtc ?? DateTime.UtcNow).ToLocalTime();
        Duration = TimeEntryCalculations.GetDuration(entry);
        DurationText = Duration.ToString(@"hh\:mm\:ss");
        ActualMinutes = TimeEntryCalculations.GetActualMinutes(Duration);
        RoundedMinutes = TimeEntryCalculations.GetRoundedMinutes(ActualMinutes);
        BillingType = matter?.BillingType ?? BillingType.Hourly;
    }

    public long MatterId { get; }
    public string Matter { get; }
    public string Hashtag { get; }
    public DateTime StartLocal { get; }
    public DateTime EndLocal { get; }
    public TimeSpan Duration { get; }
    public string DurationText { get; }
    public int ActualMinutes { get; }
    public int RoundedMinutes { get; }
    public BillingType BillingType { get; }
    public string RvgEstimateText { get; private set; } = "-";
    public string EffectiveHourlyRateText { get; private set; } = "-";
    public string BreakEvenTimeText { get; private set; } = "-";

    public void SetRvgMetrics(RvgMetrics? metrics)
    {
        if (metrics == null)
        {
            RvgEstimateText = "-";
            EffectiveHourlyRateText = "-";
            BreakEvenTimeText = "-";
            return;
        }

        RvgEstimateText = metrics.EstimateEur.ToString("N2");
        EffectiveHourlyRateText = metrics.EffectiveHourlyRateEur?.ToString("N2") ?? "-";
        BreakEvenTimeText = metrics.BreakEvenTime == null ? "-" : RvgCalculator.FormatBreakEvenTime(metrics.BreakEvenTime.Value);
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
