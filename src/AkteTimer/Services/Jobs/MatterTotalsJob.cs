using System.Security.Cryptography;
using System.Text;
using AkteTimer.Models;
using AkteTimer.ViewModels;

namespace AkteTimer.Services.Jobs;

public sealed class MatterTotalsJob
{
    public const string CurrentCalcVersion = "v2";
    private readonly DatabaseService _database;

    public MatterTotalsJob(DatabaseService database)
    {
        _database = database;
    }

    public void RecalcDailyTotal(long matterId, DateTime dayUtc)
    {
        var nowUtc = DateTime.UtcNow;
        var total = BuildDailyTotal(matterId, dayUtc, nowUtc);

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
            CalcVersion = CurrentCalcVersion
        };

        _database.UpsertMatterTotals(totals);
    }

    public void RebuildMatter(long matterId, CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var entries = _database.GetEntriesForMatter(matterId);
        var aggregates = new Dictionary<DateTime, DailyAggregate>();

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entryEndUtc = entry.EndUtc ?? nowUtc;
            if (entryEndUtc <= entry.StartUtc)
            {
                continue;
            }

            foreach (var dayStartUtc in EnumerateDays(entry.StartUtc, entryEndUtc))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var dayEndUtc = dayStartUtc.AddDays(1);
                if (entryEndUtc <= dayStartUtc || entry.StartUtc >= dayEndUtc)
                {
                    continue;
                }

                var segmentStart = entry.StartUtc < dayStartUtc ? dayStartUtc : entry.StartUtc;
                var segmentEnd = entryEndUtc > dayEndUtc ? dayEndUtc : entryEndUtc;
                var actualMinutes = TimeEntryCalculations.GetActualMinutes(segmentEnd - segmentStart);
                var roundedMinutes = TimeEntryCalculations.GetRoundedMinutes(actualMinutes);

                if (!aggregates.TryGetValue(dayStartUtc, out var aggregate))
                {
                    aggregate = new DailyAggregate();
                }

                aggregate.Add(entry, segmentStart, segmentEnd, actualMinutes, roundedMinutes);
                aggregates[dayStartUtc] = aggregate;
            }
        }

        var totals = aggregates
            .OrderBy(entry => entry.Key)
            .Select(entry => BuildDailyTotalFromAggregate(matterId, entry.Key, entry.Value, nowUtc))
            .ToList();

        _database.ReplaceMatterDailyTotals(matterId, totals);
        RecalcMatterTotal(matterId);
        _database.SetMatterTotalsInconsistent(matterId, false);
    }

    public MatterDailyTotal BuildDailyTotal(long matterId, DateTime dayUtc, DateTime nowUtc)
    {
        var dayStartUtc = dayUtc.ToUniversalTime().Date;
        var dayEndUtc = dayStartUtc.AddDays(1);
        var entries = _database.GetEntriesForMatterOverlappingRange(matterId, dayStartUtc, dayEndUtc);
        var aggregate = new DailyAggregate();

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
            var roundedMinutes = TimeEntryCalculations.GetRoundedMinutes(actualMinutes);
            aggregate.Add(entry, segmentStart, segmentEnd, actualMinutes, roundedMinutes);
        }

        return BuildDailyTotalFromAggregate(matterId, dayStartUtc, aggregate, nowUtc);
    }

    private static MatterDailyTotal BuildDailyTotalFromAggregate(
        long matterId,
        DateTime dayStartUtc,
        DailyAggregate aggregate,
        DateTime nowUtc)
    {
        return new MatterDailyTotal
        {
            MatterId = matterId,
            DayUtc = dayStartUtc,
            RoundedMinutesSum = aggregate.SumRoundedMinutes,
            Fingerprint = BuildFingerprint(aggregate),
            UpdatedAtUtc = nowUtc,
            CalcVersion = CurrentCalcVersion
        };
    }

    private static string BuildFingerprint(DailyAggregate aggregate)
    {
        var payload = string.Join(
            "|",
            aggregate.EntryCount,
            aggregate.SumActualMinutes,
            aggregate.SumRoundedMinutes,
            aggregate.ManualAdjustmentCount,
            aggregate.MinStartTicks,
            aggregate.MaxEndTicks,
            aggregate.MaxUpdatedTicks);
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(payload);
        return Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();
    }

    private static IEnumerable<DateTime> EnumerateDays(DateTime startUtc, DateTime endUtc)
    {
        var current = startUtc.ToUniversalTime().Date;
        var final = endUtc.ToUniversalTime().Date;
        while (current <= final)
        {
            yield return current;
            current = current.AddDays(1);
        }
    }

    private struct DailyAggregate
    {
        public int EntryCount { get; private set; }
        public int SumActualMinutes { get; private set; }
        public int SumRoundedMinutes { get; private set; }
        public int ManualAdjustmentCount { get; private set; }
        public long MinStartTicks { get; private set; }
        public long MaxEndTicks { get; private set; }
        public long MaxUpdatedTicks { get; private set; }

        public void Add(TimeEntry entry, DateTime segmentStartUtc, DateTime segmentEndUtc, int actualMinutes, int roundedMinutes)
        {
            EntryCount++;
            SumActualMinutes += actualMinutes;
            SumRoundedMinutes += roundedMinutes;
            if (entry.ManualAdjustment)
            {
                ManualAdjustmentCount++;
            }

            var segmentStartTicks = segmentStartUtc.ToUniversalTime().Ticks;
            var segmentEndTicks = segmentEndUtc.ToUniversalTime().Ticks;
            if (EntryCount == 1)
            {
                MinStartTicks = segmentStartTicks;
                MaxEndTicks = segmentEndTicks;
                MaxUpdatedTicks = entry.UpdatedUtc.ToUniversalTime().Ticks;
            }
            else
            {
                MinStartTicks = Math.Min(MinStartTicks, segmentStartTicks);
                MaxEndTicks = Math.Max(MaxEndTicks, segmentEndTicks);
                MaxUpdatedTicks = Math.Max(MaxUpdatedTicks, entry.UpdatedUtc.ToUniversalTime().Ticks);
            }
        }
    }
}
