using System;
using System.Collections.Generic;
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

public sealed class TodayViewModel : ViewModelBase
{
    private readonly TimeEntryService _timeEntryService;
    private readonly RelayCommand _exportCsvCommand;
    private readonly RelayCommand _exportExcelCommand;
    private List<TodayEntryViewModel> _allEntries = new();
    private DateTime _selectedDate = DateTime.Today;
    private string? _selectedMatter;
    private string? _selectedHashtag;
    private string _totalDuration = "00:00:00";
    private int _totalRoundedMinutes;

    public TodayViewModel(TimeEntryService timeEntryService)
    {
        _timeEntryService = timeEntryService;
        _exportCsvCommand = new RelayCommand(_ => ExportCsv(), _ => CanExport());
        _exportExcelCommand = new RelayCommand(_ => ExportExcel(), _ => CanExport());
        Refresh();
    }

    public ObservableCollection<TodayEntryViewModel> Entries { get; } = new();

    public ObservableCollection<FilterOption> MatterOptions { get; } = new();

    public ObservableCollection<FilterOption> HashtagOptions { get; } = new();

    public RelayCommand ExportCsvCommand => _exportCsvCommand;

    public RelayCommand ExportExcelCommand => _exportExcelCommand;

    public DateTime SelectedDate
    {
        get => _selectedDate;
        set
        {
            if (_selectedDate == value.Date)
            {
                return;
            }

            _selectedDate = value.Date;
            NotifyPropertyChanged();
            Refresh();
        }
    }

    public string? SelectedMatter
    {
        get => _selectedMatter;
        set
        {
            if (_selectedMatter == value)
            {
                return;
            }

            _selectedMatter = value;
            NotifyPropertyChanged();
            ApplyFiltersAndTotals();
        }
    }

    public string? SelectedHashtag
    {
        get => _selectedHashtag;
        set
        {
            if (_selectedHashtag == value)
            {
                return;
            }

            _selectedHashtag = value;
            NotifyPropertyChanged();
            ApplyFiltersAndTotals();
        }
    }

    public string TotalDuration
    {
        get => _totalDuration;
        private set
        {
            _totalDuration = value;
            NotifyPropertyChanged();
        }
    }

    public int TotalRoundedMinutes
    {
        get => _totalRoundedMinutes;
        private set
        {
            _totalRoundedMinutes = value;
            NotifyPropertyChanged();
        }
    }

    public void Refresh()
    {
        var matters = _timeEntryService.GetAllMatters();
        var matterIds = matters.Select(matter => matter.Id).ToList();
        var entries = matterIds.Count == 0
            ? new List<TimeEntry>()
            : _timeEntryService.GetEntriesInRange(SelectedDate, SelectedDate, matterIds);

        _allEntries = entries
            .Select(entry => new TodayEntryViewModel(entry))
            .OrderBy(entry => entry.StartLocal)
            .ToList();

        UpdateMatterOptions(matters);
        UpdateHashtagOptions(_allEntries);
        ApplyFiltersAndTotals();
    }

    private void UpdateMatterOptions(IReadOnlyList<Matter> matters)
    {
        MatterOptions.Clear();
        MatterOptions.Add(FilterOption.All);
        foreach (var matter in matters.OrderBy(matter => matter.FileRef))
        {
            MatterOptions.Add(new FilterOption(matter.FileRef, matter.FileRef));
        }

        var selected = SelectedMatter;
        if (!MatterOptions.Any(option => option.Value == selected))
        {
            SelectedMatter = null;
        }
    }

    private void UpdateHashtagOptions(IEnumerable<TodayEntryViewModel> entries)
    {
        HashtagOptions.Clear();
        HashtagOptions.Add(FilterOption.All);

        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in TimeEntryService.DefaultHashtags)
        {
            tags.Add(tag);
        }

        foreach (var tag in entries.Select(entry => entry.Hashtag).Where(tag => !string.IsNullOrWhiteSpace(tag)))
        {
            tags.Add(tag);
        }

        foreach (var tag in tags.OrderBy(tag => tag))
        {
            HashtagOptions.Add(new FilterOption(tag, tag));
        }

        var selected = SelectedHashtag;
        if (!HashtagOptions.Any(option => option.Value == selected))
        {
            SelectedHashtag = null;
        }
    }

    private void ApplyFiltersAndTotals()
    {
        Entries.Clear();
        var total = TimeSpan.Zero;
        var totalRoundedMinutes = 0;
        foreach (var entry in _allEntries.Where(MatchesFilters))
        {
            Entries.Add(entry);
            total += entry.Duration;
            totalRoundedMinutes += entry.RoundedMinutes;
        }

        TotalDuration = total.ToString(@"hh\:mm\:ss");
        TotalRoundedMinutes = totalRoundedMinutes;
        _exportCsvCommand.RaiseCanExecuteChanged();
        _exportExcelCommand.RaiseCanExecuteChanged();
    }

    private bool MatchesFilters(TodayEntryViewModel entry)
    {
        if (!string.IsNullOrWhiteSpace(SelectedMatter)
            && !string.Equals(entry.Matter, SelectedMatter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(SelectedHashtag)
            && !string.Equals(entry.Hashtag, SelectedHashtag, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private bool CanExport() => Entries.Any();

    private void ExportCsv()
    {
        var dialog = new SaveFileDialog
        {
            Title = "CSV exportieren",
            Filter = "CSV-Datei (*.csv)|*.csv",
            FileName = $"AkteTimer_Heute_{SelectedDate:yyyyMMdd}.csv"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var builder = new StringBuilder();
        WriteCsvRow(builder, "Datum", "Start", "Ende", "Akte", "Hashtag", "Ist hh:mm:ss", "Ist Minuten", "6-Min Minuten", "Notiz");

        foreach (var row in Entries)
        {
            WriteCsvRow(
                builder,
                row.DateLocal.ToString("dd.MM.yyyy"),
                row.StartLocal.ToString("HH:mm:ss"),
                row.EndLocal.ToString("HH:mm:ss"),
                row.Matter,
                row.Hashtag,
                row.DurationText,
                row.ActualMinutes.ToString(),
                row.RoundedMinutes.ToString(),
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
            FileName = $"AkteTimer_Heute_{SelectedDate:yyyyMMdd}.xlsx"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.AddWorksheet("Heute");
            sheet.Cell(1, 1).Value = "Datum";
            sheet.Cell(1, 2).Value = "Start";
            sheet.Cell(1, 3).Value = "Ende";
            sheet.Cell(1, 4).Value = "Akte";
            sheet.Cell(1, 5).Value = "Hashtag";
            sheet.Cell(1, 6).Value = "Ist hh:mm:ss";
            sheet.Cell(1, 7).Value = "Ist Minuten";
            sheet.Cell(1, 8).Value = "6-Min Minuten";
            sheet.Cell(1, 9).Value = "Notiz";

            var rowIndex = 2;
            foreach (var row in Entries)
            {
                sheet.Cell(rowIndex, 1).Value = row.DateLocal.ToString("dd.MM.yyyy");
                sheet.Cell(rowIndex, 2).Value = row.StartLocal.ToString("HH:mm:ss");
                sheet.Cell(rowIndex, 3).Value = row.EndLocal.ToString("HH:mm:ss");
                sheet.Cell(rowIndex, 4).Value = row.Matter;
                sheet.Cell(rowIndex, 5).Value = row.Hashtag;
                sheet.Cell(rowIndex, 6).Value = row.DurationText;
                sheet.Cell(rowIndex, 7).Value = row.ActualMinutes;
                sheet.Cell(rowIndex, 8).Value = row.RoundedMinutes;
                sheet.Cell(rowIndex, 9).Value = row.Note;
                rowIndex++;
            }

            sheet.Columns().AdjustToContents();
            workbook.SaveAs(dialog.FileName);
            MessageBox.Show("Excel-Export wurde erstellt.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Excel-Export fehlgeschlagen: {ex.Message}", "Export", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
}

public sealed class TodayEntryViewModel
{
    public TodayEntryViewModel(TimeEntry entry)
    {
        Entry = entry;
        Matter = entry.MatterFileRef ?? "-";
        Hashtag = entry.Hashtag ?? string.Empty;
        Note = entry.Note ?? string.Empty;
        StartLocal = entry.StartUtc.ToLocalTime();
        EndLocal = (entry.EndUtc ?? DateTime.UtcNow).ToLocalTime();
        DateLocal = StartLocal.Date;
        Duration = TimeEntryCalculations.GetDuration(entry);
        DurationText = Duration.ToString(@"hh\:mm\:ss");
        ActualMinutes = TimeEntryCalculations.GetActualMinutes(Duration);
        RoundedMinutes = TimeEntryCalculations.GetRoundedMinutes(ActualMinutes);
    }

    public TimeEntry Entry { get; }
    public string Matter { get; }
    public string Hashtag { get; }
    public string Note { get; }
    public DateTime DateLocal { get; }
    public DateTime StartLocal { get; }
    public DateTime EndLocal { get; }
    public TimeSpan Duration { get; }
    public string DurationText { get; }
    public int ActualMinutes { get; }
    public int RoundedMinutes { get; }
}

public sealed class FilterOption
{
    public static FilterOption All { get; } = new("(Alle)", null);

    public FilterOption(string label, string? value)
    {
        Label = label;
        Value = value;
    }

    public string Label { get; }
    public string? Value { get; }
}
