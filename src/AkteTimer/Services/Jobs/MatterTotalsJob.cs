using AkteTimer.Models;
using AkteTimer.ViewModels;

namespace AkteTimer.Services.Jobs;

public sealed class MatterTotalsJob
{
    private readonly DatabaseService _database;

    public MatterTotalsJob(DatabaseService database)
    {
        _database = database;
    }

    public void RecalcDailyTotal(long matterId, DateTime dayUtc)
    {
        var dayStartUtc = dayUtc.ToUniversalTime().Date;
        var dayEndUtc = dayStartUtc.AddDays(1);
        var entries = _database.GetEntriesForMatterOverlappingRange(matterId, dayStartUtc, dayEndUtc);
        var nowUtc = DateTime.UtcNow;
        var totalRoundedMinutes = 0;

        foreach (var entry in entries)
        {
            var entryEndUtc = entry.EndUtc ?? nowUtc;
            if (entryEndUtc <= dayStartUtc || entry.StartUtc >= dayEndUtc)
            {
                continue;
            }

            var segmentStart = entry.StartUtc < dayStartUtc ? dayStartUtc : entry.StartUtc;
            var segmentEnd = entryEndUtc > dayEndUtc ? dayEndUtc : entryEndUtc;
            var actualMinutes = TimeEntryCalculations.GetActualMinutes(segmentEnd - segmentStart);
            totalRoundedMinutes += TimeEntryCalculations.GetRoundedMinutes(actualMinutes);
        }

        var total = new MatterDailyTotal
        {
            MatterId = matterId,
            DayUtc = dayStartUtc,
            RoundedMinutesSum = totalRoundedMinutes,
            Fingerprint = null,
            UpdatedAtUtc = nowUtc,
            CalcVersion = null
        };

        _database.UpsertMatterDailyTotal(total);
    }

    public void RecalcMatterTotal(long matterId)
    {
        var (totalRoundedMinutes, maxUpdatedAtUtc) = _database.GetMatterDailyTotalsAggregate(matterId);
        var totals = new MatterTotals
        {
            MatterId = matterId,
            TotalRoundedMinutesAllTime = totalRoundedMinutes,
            DailyTotalsMaxUpdatedAt = maxUpdatedAtUtc?.ToString("o"),
            CalculatedAtUtc = DateTime.UtcNow,
            CalcVersion = null
        };

        _database.UpsertMatterTotals(totals);
    }
}
