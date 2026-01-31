using AkteTimer.Models;
using AkteTimer.ViewModels;

namespace AkteTimer.Services;

public sealed class BillingService
{
    private readonly DatabaseService _database;

    public BillingService(DatabaseService database)
    {
        _database = database;
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
}
