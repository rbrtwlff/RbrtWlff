using System.Diagnostics;
using AkteTimer.Models;

namespace AkteTimer.Services.Jobs;

public sealed class MatterTotalsVerifierJob
{
    private readonly DatabaseService _database;
    private readonly MatterTotalsJob _totalsJob;
    private readonly MatterTotalsQueue _totalsQueue;

    public MatterTotalsVerifierJob(DatabaseService database, MatterTotalsQueue totalsQueue)
    {
        _database = database;
        _totalsJob = new MatterTotalsJob(database);
        _totalsQueue = totalsQueue;
    }

    public void Run(VerifyBudget budget, CancellationToken cancellationToken)
    {
        if (budget.DailySamples <= 0 && budget.MatterSamples <= 0)
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        VerifyDailyTotals(budget, stopwatch, cancellationToken);
        VerifyMatterTotals(budget, stopwatch, cancellationToken);
    }

    private void VerifyDailyTotals(VerifyBudget budget, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        if (budget.DailySamples <= 0)
        {
            return;
        }

        var samples = _database.GetMatterDailyTotalsSample(budget.DailySamples);
        var nowUtc = DateTime.UtcNow;
        foreach (var sample in samples)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsBudgetExceeded(budget, stopwatch))
            {
                return;
            }

            var recalculated = _totalsJob.BuildDailyTotal(sample.MatterId, sample.DayUtc, nowUtc);
            if (!IsDailyTotalConsistent(sample, recalculated))
            {
                MarkInconsistent(sample.MatterId);
                return;
            }
        }
    }

    private void VerifyMatterTotals(VerifyBudget budget, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        if (budget.MatterSamples <= 0)
        {
            return;
        }

        var matterIds = _database.GetMatterIdSamples(budget.MatterSamples);
        foreach (var matterId in matterIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsBudgetExceeded(budget, stopwatch))
            {
                return;
            }

            var storedTotals = _database.GetMatterTotals(matterId);
            var (sumRoundedMinutes, maxUpdatedAtUtc) = _database.GetMatterDailyTotalsAggregate(matterId);
            var maxUpdatedText = maxUpdatedAtUtc?.ToString("o");

            if (storedTotals == null ||
                storedTotals.TotalRoundedMinutesAllTime != sumRoundedMinutes ||
                !string.Equals(storedTotals.DailyTotalsMaxUpdatedAt, maxUpdatedText, StringComparison.OrdinalIgnoreCase))
            {
                MarkInconsistent(matterId);
                return;
            }
        }
    }

    private static bool IsDailyTotalConsistent(MatterDailyTotal stored, MatterDailyTotal recalculated)
    {
        return stored.RoundedMinutesSum == recalculated.RoundedMinutesSum
            && string.Equals(stored.Fingerprint, recalculated.Fingerprint, StringComparison.OrdinalIgnoreCase);
    }

    private void MarkInconsistent(long matterId)
    {
        _database.SetMatterTotalsInconsistent(matterId, true);
        _totalsQueue.EnqueueMatterRebuild(matterId);
    }

    private static bool IsBudgetExceeded(VerifyBudget budget, Stopwatch stopwatch)
    {
        if (budget.MaxDuration <= TimeSpan.Zero)
        {
            return false;
        }

        return stopwatch.Elapsed > budget.MaxDuration;
    }
}
