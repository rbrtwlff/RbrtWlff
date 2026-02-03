using System.Collections.Concurrent;
using System.Threading;

namespace AkteTimer.Services.Jobs;

public sealed class BackgroundJobQueue : IDisposable
{
    private readonly BlockingCollection<QueuedJob> _queue = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Thread _worker;

    public BackgroundJobQueue(string name)
    {
        _worker = new Thread(Run)
        {
            IsBackground = true,
            Name = name,
            Priority = ThreadPriority.BelowNormal
        };
        _worker.Start();
    }

    public void Enqueue(string description, Action<CancellationToken> action, CancellationToken cancellationToken = default)
    {
        if (_shutdown.IsCancellationRequested)
        {
            return;
        }

        var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token, cancellationToken);
        _queue.Add(new QueuedJob(description, action, linkedSource));
    }

    public void CancelAll()
    {
        if (_shutdown.IsCancellationRequested)
        {
            return;
        }

        _shutdown.Cancel();
        while (_queue.TryTake(out var job))
        {
            job.Dispose();
        }
    }

    private void Run()
    {
        try
        {
            foreach (var job in _queue.GetConsumingEnumerable(_shutdown.Token))
            {
                if (job.Token.IsCancellationRequested)
                {
                    job.Dispose();
                    continue;
                }

                try
                {
                    job.Action(job.Token);
                }
                catch (OperationCanceledException)
                {
                    // Ignore cancellations.
                }
                catch (Exception ex)
                {
                    LogService.LogException(ex, $"Background job failed: {job.Description}");
                }
                finally
                {
                    job.Dispose();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested.
        }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        CancelAll();
    }

    private sealed class QueuedJob : IDisposable
    {
        public QueuedJob(string description, Action<CancellationToken> action, CancellationTokenSource tokenSource)
        {
            Description = description;
            Action = action;
            TokenSource = tokenSource;
        }

        public string Description { get; }

        public Action<CancellationToken> Action { get; }

        public CancellationTokenSource TokenSource { get; }

        public CancellationToken Token => TokenSource.Token;

        public void Dispose() => TokenSource.Dispose();
    }
}
