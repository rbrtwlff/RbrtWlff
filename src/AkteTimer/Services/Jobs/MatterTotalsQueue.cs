using AkteTimer.Models;

namespace AkteTimer.Services.Jobs;

public sealed class MatterTotalsQueue : IDisposable
{
    private readonly BackgroundJobQueue _queue;
    private readonly MatterTotalsJob _job;
    private readonly HashSet<(long MatterId, DateTime DayUtc)> _dirtyDays = new();
    private readonly object _sync = new();

    public MatterTotalsQueue(DatabaseService database)
    {
        _queue = new BackgroundJobQueue("MatterTotalsQueue");
        _job = new MatterTotalsJob(database);
    }

    public void EnqueueForEntry(TimeEntry entry, DateTime? endOverrideUtc = null)
    {
        EnqueueForRange(entry.MatterId, entry.StartUtc, endOverrideUtc ?? entry.EndUtc);
    }

    public void EnqueueForRange(long matterId, DateTime startUtc, DateTime? endUtc)
    {
        var end = endUtc ?? DateTime.UtcNow;
        if (end < startUtc)
        {
            (startUtc, end) = (end, startUtc);
        }

        foreach (var dayUtc in EnumerateDays(startUtc, end))
        {
            if (!MarkDirty(matterId, dayUtc))
            {
                continue;
            }

            var day = dayUtc;
            _queue.Enqueue(
                $"RecalcDailyTotal:{matterId}:{day:yyyy-MM-dd}",
                _ =>
                {
                    _job.RecalcDailyTotal(matterId, day);
                    ClearDirty(matterId, day);
                });
        }

        _queue.Enqueue(
            $"RecalcMatterTotal:{matterId}",
            _ => _job.RecalcMatterTotal(matterId));
    }

    public void EnqueueMatterTotal(long matterId)
    {
        _queue.Enqueue(
            $"RecalcMatterTotal:{matterId}",
            _ => _job.RecalcMatterTotal(matterId));
    }

    public void EnqueueMatterRebuild(long matterId)
    {
        _queue.Enqueue(
            $"RebuildMatter:{matterId}",
            token => _job.RebuildMatter(matterId, token));
    }

    public void CancelAll() => _queue.CancelAll();

    public void Dispose() => _queue.Dispose();

    private static IEnumerable<DateTime> EnumerateDays(DateTime startUtc, DateTime endUtc)
    {
        var current = startUtc.Date;
        var final = endUtc.Date;
        while (current <= final)
        {
            yield return current;
            current = current.AddDays(1);
        }
    }

    private bool MarkDirty(long matterId, DateTime dayUtc)
    {
        lock (_sync)
        {
            return _dirtyDays.Add((matterId, dayUtc.Date));
        }
    }

    private void ClearDirty(long matterId, DateTime dayUtc)
    {
        lock (_sync)
        {
            _dirtyDays.Remove((matterId, dayUtc.Date));
        }
    }
}
