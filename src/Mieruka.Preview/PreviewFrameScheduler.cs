using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Mieruka.Preview;

public sealed class PreviewFrameScheduler
{
    private readonly object _gate = new();
    private readonly Stopwatch _stopwatch = new();
    private readonly long _targetFrameTicks;

    private CancellationTokenSource? _cts;
    private Task? _worker;
    private long _nextFrameAtTicks;
    private long _completedFrames;
    private long _totalProcessingTicks;

    public PreviewFrameScheduler(double targetFramesPerSecond)
    {
        if (double.IsNaN(targetFramesPerSecond) || double.IsInfinity(targetFramesPerSecond) || targetFramesPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetFramesPerSecond));
        }

        TargetFps = targetFramesPerSecond;
        _targetFrameTicks = Math.Max(1, (long)(Stopwatch.Frequency / targetFramesPerSecond));
        _stopwatch.Start();
    }

    public double TargetFps { get; }

    public void Start(Func<CancellationToken, Task> frameProducer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frameProducer);

        lock (_gate)
        {
            if (_cts is not null)
            {
                throw new InvalidOperationException("Frame scheduler already started.");
            }

            _nextFrameAtTicks = 0;
            _completedFrames = 0;
            _totalProcessingTicks = 0;

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _worker = Task.Run(() => RunAsync(frameProducer, _cts.Token), CancellationToken.None);
        }
    }

    public bool TryBeginFrame(out long timestampTicks)
    {
        lock (_gate)
        {
            var now = _stopwatch.ElapsedTicks;
            if (_nextFrameAtTicks == 0)
            {
                _nextFrameAtTicks = now;
            }

            if (now < _nextFrameAtTicks)
            {
                timestampTicks = 0;
                return false;
            }

            timestampTicks = now;
            _nextFrameAtTicks = now + _targetFrameTicks;
            return true;
        }
    }

    public void EndFrame(long startTimestampTicks)
    {
        if (startTimestampTicks <= 0)
        {
            return;
        }

        var elapsedTicks = _stopwatch.ElapsedTicks - startTimestampTicks;
        if (elapsedTicks < 0)
        {
            return;
        }

        lock (_gate)
        {
            _completedFrames++;
            _totalProcessingTicks += elapsedTicks;
        }
    }

    public async Task StopAsync()
    {
        Task? worker;
        CancellationTokenSource? cts;

        lock (_gate)
        {
            cts = _cts;
            worker = _worker;
            _cts = null;
            _worker = null;
        }

        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
            if (worker is not null)
            {
                await worker.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested.
        }
        finally
        {
            cts.Dispose();
        }
    }

    public PreviewFrameSchedulerMetrics GetMetricsAndReset()
    {
        lock (_gate)
        {
            var metrics = new PreviewFrameSchedulerMetrics(_completedFrames, _totalProcessingTicks, Stopwatch.Frequency);
            _completedFrames = 0;
            _totalProcessingTicks = 0;
            return metrics;
        }
    }

    private async Task RunAsync(Func<CancellationToken, Task> frameProducer, CancellationToken cancellationToken)
    {
        await Task.Yield();

        while (!cancellationToken.IsCancellationRequested)
        {
            long frameStartTimestamp;
            if (!TryBeginFrame(out frameStartTimestamp))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(1), cancellationToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                await frameProducer(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                // Swallow unexpected exceptions to keep the loop alive.
            }
            finally
            {
                EndFrame(frameStartTimestamp);
            }
        }
    }
}

public readonly struct PreviewFrameSchedulerMetrics
{
    public PreviewFrameSchedulerMetrics(long frames, long totalProcessingTicks, long frequency)
    {
        Frames = frames;
        AverageProcessingMilliseconds = frames <= 0
            ? 0
            : totalProcessingTicks * 1000d / frequency / frames;
    }

    public long Frames { get; }

    public double AverageProcessingMilliseconds { get; }
}
