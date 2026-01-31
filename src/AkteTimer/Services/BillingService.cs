using System.Globalization;
using AkteTimer.Models;
using AkteTimer.ViewModels;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace AkteTimer.Services;

public sealed class BillingService
{
    private readonly DatabaseService _database;

    public BillingService(DatabaseService database)
    {
        _database = database;
    }

    public static string ComputeRvgSignature(Matter matter)
    {
        var culture = CultureInfo.InvariantCulture;
        var subjectValue = matter.SubjectValueEur.ToString("G29", culture);
        var customFeeFactor = matter.CustomFeeFactor.HasValue
            ? matter.CustomFeeFactor.Value.ToString("G29", culture)
            : "null";
        var businessFee = matter.BusinessFee13Enabled ? "1" : "0";
        var termFee = matter.TermFee12Enabled ? "1" : "0";
        var settlement10 = matter.SettlementFee10Enabled ? "1" : "0";
        var settlement15 = matter.SettlementFee15Enabled ? "1" : "0";

        return $"SV={subjectValue};CF={customFeeFactor};B13={businessFee};T12={termFee};S10={settlement10};S15={settlement15}";
    }

    public (long BatchId, List<long> CaseIds) CreateBillingBatchDraft(IEnumerable<long> timeEntryIds)
    {
        var entryIdList = timeEntryIds?.Distinct().ToList() ?? new List<long>();
        var entries = _database.GetTimeEntriesByIds(entryIdList, onlyCompleted: true);
        var eligibleEntries = entries.Where(entry => !entry.Billed).ToList();

        var batch = _database.CreateBillingBatch(DateTime.UtcNow);
        if (eligibleEntries.Count == 0)
        {
            return (batch.Id, new List<long>());
        }

        var cases = new List<(long CaseId, string FileRef)>();
        foreach (var group in eligibleEntries.GroupBy(entry => entry.MatterId))
        {
            var matter = _database.GetMatterById(group.Key)
                         ?? throw new InvalidOperationException("Matter nicht gefunden.");

            var trackedMinutes = group.Sum(entry =>
            {
                var duration = TimeEntryCalculations.GetDuration(entry);
                var actualMinutes = TimeEntryCalculations.GetActualMinutes(duration);
                return TimeEntryCalculations.GetRoundedMinutes(actualMinutes);
            });

            var trackedAmount = matter.BillingType == BillingType.Hourly
                ? (trackedMinutes / 60m) * matter.HourlyRateEurPerHour
                : 0m;

            var billingCase = new BillingCase
            {
                BatchId = batch.Id,
                MatterId = matter.Id,
                BillingType = matter.BillingType,
                ApprovedUtc = null,
                TrackedMinutes = trackedMinutes,
                DummyMinutes = 0,
                TotalMinutes = trackedMinutes,
                TrackedAmount = trackedAmount,
                DummyAmount = 0m,
                TotalAmount = trackedAmount,
                NoteForStaff = null,
                RvgSignature = null,
                RvgTotal = 0m,
                RvgIsDifference = false,
                RvgBaseSignature = null,
                RvgBaseTotal = 0m
            };

            var createdCase = _database.CreateBillingCase(billingCase);
            cases.Add((createdCase.Id, matter.FileRef));
        }

        var sortedCaseIds = cases
            .OrderBy(item => item.FileRef, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.CaseId)
            .ToList();

        return (batch.Id, sortedCaseIds);
    }

    public void ExportBillingBatchToPdf(long batchId, string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("PDF-Dateipfad darf nicht leer sein.", nameof(filePath));
        }

        var batch = _database.GetBillingBatchById(batchId)
                    ?? throw new InvalidOperationException("Abrechnungsbatch nicht gefunden.");

        var caseData = _database.GetBillingCasesForBatch(batchId)
            .Select(billingCase =>
            {
                var matter = _database.GetMatterById(billingCase.MatterId)
                             ?? throw new InvalidOperationException("Akte nicht gefunden.");
                var timeEntries = _database.GetEntriesForMatter(matter.Id);
                var rvgSignature = ComputeRvgSignature(matter);
                var rvgFeeSummary = BuildRvgFeeSummary(matter);
                return new BillingCasePdfData(billingCase, matter, timeEntries, rvgSignature, rvgFeeSummary);
            })
            .OrderBy(item => item.Matter.FileRef, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        QuestPDF.Settings.License = LicenseType.Community;
        var document = new BillingBatchPdfDocument(batch, caseData);
        document.GeneratePdf(filePath);

        _database.UpdateBillingBatchPdfPath(batchId, filePath);
    }

    private static string BuildRvgFeeSummary(Matter matter)
    {
        var parts = new List<string>
        {
            matter.BusinessFee13Enabled ? "Geschäft 1,3" : "Geschäft 1,3 aus",
            matter.TermFee12Enabled ? "Termin 1,2" : "Termin 1,2 aus",
            matter.SettlementFee10Enabled ? "Vergleich 1,0" : "Vergleich 1,0 aus",
            matter.SettlementFee15Enabled ? "Vergleich 1,5" : "Vergleich 1,5 aus"
        };

        if (matter.CustomFeeFactor.HasValue)
        {
            parts.Add($"Custom-Faktor {matter.CustomFeeFactor.Value.ToString("N2", CultureInfo.GetCultureInfo("de-DE"))}");
        }
        else
        {
            parts.Add("Custom-Faktor aus");
        }

        return string.Join(" · ", parts);
    }
}
