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

    public void RecalculateRvgSnapshotsForMatter(long matterId)
    {
        RecalculateRvgSnapshots(matterId);
    }

    public void RecalculateRvgSnapshotsForAll()
    {
        RecalculateRvgSnapshots(null);
    }

    private void RecalculateRvgSnapshots(long? matterId)
    {
        var cases = _database.GetFinalizedRvgBillingCases(matterId);
        if (cases.Count == 0)
        {
            return;
        }

        var feeTable = new RvgFeeTableService();
        foreach (var entry in cases)
        {
            var signature = entry.RvgSignature;
            if (string.IsNullOrWhiteSpace(signature))
            {
                var matter = _database.GetMatterById(entry.MatterId)
                             ?? throw new InvalidOperationException("Akte nicht gefunden.");
                signature = ComputeRvgSignature(matter);
            }

            var breakdown = CalculateBreakdownFromSignature(signature, feeTable);
            var total = breakdown.Total;

            var baseSignature = entry.RvgBaseSignature;
            var baseTotal = 0m;
            var delta = total;
            if (entry.RvgIsDifference)
            {
                if (string.IsNullOrWhiteSpace(baseSignature))
                {
                    throw new InvalidOperationException("RVG-Differenzabrechnung ohne Basissignatur.");
                }

                var baseBreakdown = CalculateBreakdownFromSignature(baseSignature, feeTable);
                baseTotal = baseBreakdown.Total;
                delta = total - baseTotal;
                if (delta < 0m)
                {
                    delta = 0m;
                }
            }

            _database.UpdateBillingCaseRvgData(
                entry.BillingCaseId,
                signature,
                delta,
                entry.RvgIsDifference,
                baseSignature,
                baseTotal);

            var snapshotTotal = entry.RvgIsDifference ? baseTotal + delta : total;
            var breakdownJson = RvgBreakdownSerializer.Serialize(breakdown);
            var snapshot = _database.GetRvgBillingSnapshotForBatch(entry.BatchId, entry.MatterId);
            if (snapshot != null)
            {
                _database.UpdateRvgBillingSnapshot(snapshot.Id, signature, snapshotTotal, breakdownJson);
            }
            else if (snapshotTotal > 0m)
            {
                _database.InsertRvgBillingSnapshot(new RvgBillingSnapshot
                {
                    MatterId = entry.MatterId,
                    BilledUtc = entry.BilledUtc,
                    Signature = signature,
                    Total = snapshotTotal,
                    BatchId = entry.BatchId,
                    BreakdownJson = breakdownJson
                });
            }
        }
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

    private static RvgBreakdown CalculateBreakdownFromSignature(string signature, RvgFeeTableService feeTableService)
    {
        if (!TryParseRvgSignature(signature, out var data))
        {
            throw new InvalidOperationException($"RVG-Signatur ungültig: {signature}");
        }

        var matter = new Matter
        {
            BillingType = BillingType.Rvg,
            SubjectValueEur = data.SubjectValueEur,
            CustomFeeFactor = data.CustomFeeFactor,
            BusinessFee13Enabled = data.BusinessFee13Enabled,
            TermFee12Enabled = data.TermFee12Enabled,
            SettlementFee10Enabled = data.SettlementFee10Enabled,
            SettlementFee15Enabled = data.SettlementFee15Enabled
        };

        return RvgCalculator.CalculateBreakdown(matter, feeTableService);
    }

    private static bool TryParseRvgSignature(string signature, out RvgSignatureData data)
    {
        data = default;
        if (string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        decimal? subjectValue = null;
        decimal? customFeeFactor = null;
        bool? businessFee = null;
        bool? termFee = null;
        bool? settlement10 = null;
        bool? settlement15 = null;

        var parts = signature.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var pair = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length != 2)
            {
                return false;
            }

            switch (pair[0])
            {
                case "SV":
                    if (!decimal.TryParse(pair[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var sv))
                    {
                        return false;
                    }
                    subjectValue = sv;
                    break;
                case "CF":
                    if (string.Equals(pair[1], "null", StringComparison.OrdinalIgnoreCase))
                    {
                        customFeeFactor = null;
                    }
                    else if (decimal.TryParse(pair[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var cf))
                    {
                        customFeeFactor = cf;
                    }
                    else
                    {
                        return false;
                    }
                    break;
                case "B13":
                    businessFee = pair[1] == "1";
                    break;
                case "T12":
                    termFee = pair[1] == "1";
                    break;
                case "S10":
                    settlement10 = pair[1] == "1";
                    break;
                case "S15":
                    settlement15 = pair[1] == "1";
                    break;
                default:
                    return false;
            }
        }

        if (subjectValue == null || businessFee == null || termFee == null || settlement10 == null || settlement15 == null)
        {
            return false;
        }

        data = new RvgSignatureData(
            subjectValue.Value,
            customFeeFactor,
            businessFee.Value,
            termFee.Value,
            settlement10.Value,
            settlement15.Value);
        return true;
    }

    private readonly record struct RvgSignatureData(
        decimal SubjectValueEur,
        decimal? CustomFeeFactor,
        bool BusinessFee13Enabled,
        bool TermFee12Enabled,
        bool SettlementFee10Enabled,
        bool SettlementFee15Enabled);
}
