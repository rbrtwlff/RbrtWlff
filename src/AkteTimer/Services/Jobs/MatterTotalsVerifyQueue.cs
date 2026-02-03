using System.Threading;
using Timer = System.Threading.Timer;

namespace AkteTimer.Services.Jobs;

public sealed class MatterTotalsVerifyQueue : IDisposable
{
    private static readonly TimeSpan PeriodicInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan IdleCooldown = TimeSpan.FromMinutes(5);
    private static readonly VerifyBudget PeriodicBudget = new(3, 3, TimeSpan.FromSeconds(3));
    private static readonly VerifyBudget IdleBudget = new(6, 5, TimeSpan.FromSeconds(5));

    private readonly BackgroundJobQueue _queue;
    private readonly MatterTotalsVerifierJob _job;
    private readonly Timer _timer;
    private readonly object _sync = new();
    private DateTime _lastIdleRunUtc = DateTime.MinValue;
    private int _pending;

    public MatterTotalsVerifyQueue(DatabaseService database, MatterTotalsQueue totalsQueue)
    {
        _queue = new BackgroundJobQueue("MatterTotalsVerifyQueue");
        _job = new MatterTotalsVerifierJob(database, totalsQueue);
        _timer = new Timer(_ => EnqueueVerification(PeriodicBudget, "Periodic"), null, PeriodicInterval, PeriodicInterval);
    }

    public void NotifyIdle(bool isRunning)
    {
        if (isRunning)
        {
            return;
        }

        lock (_sync)
        {
            var nowUtc = DateTime.UtcNow;
            if (nowUtc - _lastIdleRunUtc < IdleCooldown)
            {
                return;
            }

            _lastIdleRunUtc = nowUtc;
        }

        EnqueueVerification(IdleBudget, "Idle");
    }

    public void Dispose()
    {
        _timer.Dispose();
        _queue.Dispose();
    }

    private void EnqueueVerification(VerifyBudget budget, string reason)
    {
        if (Interlocked.Exchange(ref _pending, 1) == 1)
        {
            return;
        }

        _queue.Enqueue(
            $"VerifyTotals:{reason}",
            token =>
            {
                try
                {
                    _job.Run(budget, token);
                }
                finally
                {
                    Interlocked.Exchange(ref _pending, 0);
                }
            });
    }
}
