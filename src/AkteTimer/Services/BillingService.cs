using System.Globalization;
using System.IO;
using AkteTimer.Models;
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
        var selectedEntries = _database.GetTimeEntriesByIds(entryIdList, onlyCompleted: true)
            .Where(entry => !entry.Billed)
            .ToList();

        var batch = _database.CreateBillingBatch(DateTime.UtcNow);
        if (selectedEntries.Count == 0)
        {
            return (batch.Id, new List<long>());
        }

        var selectedCountByMatter = selectedEntries
            .GroupBy(entry => entry.MatterId)
            .ToDictionary(group => group.Key, group => group.Count());

        var cases = new List<(long CaseId, string FileRef)>();
        foreach (var matterId in selectedCountByMatter.Keys)
        {
            var matter = _database.GetMatterById(matterId)
                         ?? throw new InvalidOperationException("Matter nicht gefunden.");

            // Die Batch-Menge wird pro Matter bewusst erweitert: neben der Auswahl
            // werden alle aktuell abrechenbaren (completed + unbilled) Einträge einbezogen.
            var billableEntries = _database.GetBillableEntriesForMatter(matterId);
            var trackedMinutes = billableEntries.Sum(entry =>
            {
                var duration = AkteTimer.ViewModels.TimeEntryCalculations.GetDuration(entry);
                var actualMinutes = AkteTimer.ViewModels.TimeEntryCalculations.GetActualMinutes(duration);
                return AkteTimer.ViewModels.TimeEntryCalculations.GetRoundedMinutes(actualMinutes);
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
                RvgBaseTotal = 0m,
                SelectedEntryCount = selectedCountByMatter[matterId],
                IncludedEntryCount = billableEntries.Count
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

        var rvgFeeTableService = new RvgFeeTableService();
        var caseData = _database.GetBillingCasesForBatch(batchId)
            .Select(billingCase =>
            {
                var matter = _database.GetMatterById(billingCase.MatterId)
                             ?? throw new InvalidOperationException("Akte nicht gefunden.");
                var timeEntries = _database.GetBillableEntriesForMatter(matter.Id);
                var rvgSignature = ComputeRvgSignature(matter);
                var rvgFeeSummary = BuildRvgFeeSummary(matter);
                RvgBreakdown? breakdown = null;
                string? breakdownNote = null;
                decimal displayTotal = billingCase.RvgTotal;

                if (billingCase.BillingType == BillingType.Rvg)
                {
                    var snapshotForBatch = _database.GetRvgBillingSnapshotForBatch(batchId, matter.Id);
                    var snapshot = snapshotForBatch;
                    var usedSnapshot = snapshotForBatch != null;
                    if (snapshot == null)
                    {
                        var latestSnapshot = _database.GetLatestRvgBillingSnapshot(matter.Id);
                        if (latestSnapshot != null && string.Equals(latestSnapshot.Signature, rvgSignature, StringComparison.Ordinal))
                        {
                            snapshot = latestSnapshot;
                            usedSnapshot = true;
                        }
                    }

                    if (snapshot != null)
                    {
                        breakdown = RvgBreakdownSerializer.Deserialize(snapshot.BreakdownJson);
                        if (breakdown == null)
                        {
                            breakdownNote = "ohne Aufschlüsselung, Altbestand";
                        }

                        displayTotal = snapshot.Total;
                    }

                    if (!usedSnapshot)
                    {
                        breakdown ??= RvgCalculator.CalculateBreakdown(matter, rvgFeeTableService);
                    }

                    if (!usedSnapshot && breakdown != null)
                    {
                        displayTotal = breakdown.Total;
                    }
                }

                return new BillingCasePdfData(
                    billingCase,
                    matter,
                    timeEntries,
                    rvgSignature,
                    rvgFeeSummary,
                    breakdown,
                    breakdownNote,
                    displayTotal);
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

    public void FinalizeBatch(long batchId)
    {
        var batch = _database.GetBillingBatchById(batchId)
                    ?? throw new InvalidOperationException("Abrechnungsbatch nicht gefunden.");

        if (batch.FinalizedUtc.HasValue)
        {
            throw new InvalidOperationException("Der Abrechnungsbatch wurde bereits finalisiert.");
        }

        if (string.IsNullOrWhiteSpace(batch.PdfPath))
        {
            throw new InvalidOperationException("Der Abrechnungsbatch muss vor der Finalisierung als PDF exportiert werden.");
        }

        var cases = _database.GetBillingCasesForBatch(batchId);
        var rvgFeeTableService = new RvgFeeTableService();
        var finalizedUtc = DateTime.UtcNow;
        var matterIds = cases
            .Select(billingCase => billingCase.MatterId)
            .Distinct()
            .ToList();

        var snapshots = new List<RvgBillingSnapshot>();
        foreach (var billingCase in cases)
        {
            if (billingCase.BillingType != BillingType.Rvg)
            {
                continue;
            }

            var matter = _database.GetMatterById(billingCase.MatterId)
                         ?? throw new InvalidOperationException("Akte nicht gefunden.");
            var computedSignature = ComputeRvgSignature(matter);
            var latestSnapshot = _database.GetLatestRvgBillingSnapshot(matter.Id);
            if (latestSnapshot != null
                && string.Equals(latestSnapshot.Signature, computedSignature, StringComparison.Ordinal))
            {
                // Kein Delta ist kein Fehler: vorhandener Snapshot bleibt gültig.
                continue;
            }

            var signatureToUse = !string.IsNullOrWhiteSpace(billingCase.RvgSignature)
                ? billingCase.RvgSignature
                : computedSignature;
            if (string.IsNullOrWhiteSpace(billingCase.RvgSignature))
            {
                // Datenheilung: Signatur speichern, obwohl kein neuer Snapshot erzwungen wird.
                _database.UpdateBillingCaseRvgData(
                    billingCase.Id,
                    signatureToUse,
                    billingCase.RvgTotal,
                    billingCase.RvgIsDifference,
                    billingCase.RvgBaseSignature,
                    billingCase.RvgBaseTotal);
            }

            var breakdown = RvgCalculator.CalculateBreakdown(matter, rvgFeeTableService);
            var breakdownJson = RvgBreakdownSerializer.Serialize(breakdown);
            var total = billingCase.RvgIsDifference
                ? billingCase.RvgBaseTotal + billingCase.RvgTotal
                : billingCase.RvgTotal;
            if (total <= 0m)
            {
                // Kein abrechenbarer Betrag: kein Snapshot erforderlich.
                continue;
            }

            snapshots.Add(new RvgBillingSnapshot
            {
                MatterId = billingCase.MatterId,
                BilledUtc = finalizedUtc,
                Signature = signatureToUse,
                Total = total,
                BatchId = batchId,
                BreakdownJson = breakdownJson
            });
        }

        _database.FinalizeBillingBatch(batchId, matterIds, snapshots, finalizedUtc);
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
