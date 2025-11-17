using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Mieruka.Preview;

public sealed class PreviewFrameScheduler
{
    private readonly object _gate = new();
    private readonly Stopwatch _stopwatch = new();
    private readonly TimeSpan _frameInterval;

    private CancellationTokenSource? _cts;
    private Task? _worker;

    private long _nextFrameAtTicks;
    private long _framesThisWindow;
    private TimeSpan _windowStart;
    private double _currentFps;
    private double _lastFrameDurationMs;
    private long _totalFrames;
    private long _completedFrames;
    private long _totalProcessingTicks;

    public PreviewFrameScheduler(double targetFramesPerSecond)
    {
        if (double.IsNaN(targetFramesPerSecond) || double.IsInfinity(targetFramesPerSecond) || targetFramesPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetFramesPerSecond));
        }

        TargetFps = targetFramesPerSecond;
        _frameInterval = TimeSpan.FromSeconds(1d / TargetFps);
        _windowStart = TimeSpan.Zero;

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
            _framesThisWindow = 0;
            _windowStart = _stopwatch.Elapsed;

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _worker = Task.Run(() => RunAsync(frameProducer, _cts.Token), CancellationToken.None);
        }
    }

    public bool TryBeginFrame(out long timestampTicks)
    {
        lock (_gate)
        {
            var now = _stopwatch.Elapsed;
            timestampTicks = now.Ticks;

            if (_nextFrameAtTicks == 0)
            {
                _nextFrameAtTicks = now.Ticks;
            }

            if (now.Ticks < _nextFrameAtTicks)
            {
                return false;
            }

            if (_windowStart == TimeSpan.Zero)
            {
                _windowStart = now;
            }

            _nextFrameAtTicks = now.Ticks + _frameInterval.Ticks;
            return true;
        }
    }

    public void EndFrame(long frameStartTimestampTicks)
    {
        lock (_gate)
        {
            var nowTicks = _stopwatch.ElapsedTicks;
            var frameDurationTicks = Math.Max(0, nowTicks - frameStartTimestampTicks);

            _framesThisWindow++;
            _totalFrames++;
            _completedFrames++;
            _totalProcessingTicks += frameDurationTicks;
            _lastFrameDurationMs = frameDurationTicks / (double)TimeSpan.TicksPerMillisecond;

            var now = TimeSpan.FromTicks(nowTicks);
            var windowElapsed = now - _windowStart;
            if (windowElapsed.TotalSeconds >= 1)
            {
                _currentFps = _framesThisWindow / windowElapsed.TotalSeconds;
                _framesThisWindow = 0;
                _windowStart = now;
            }
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

    public void Stop()
    {
        StopAsync().GetAwaiter().GetResult();
    }

    public PreviewFrameSchedulerMetrics GetMetricsAndReset()
    {
        lock (_gate)
        {
            var averageProcessingMs = _completedFrames == 0
                ? 0
                : (_totalProcessingTicks / (double)_completedFrames) / TimeSpan.TicksPerMillisecond;

            var metrics = new PreviewFrameSchedulerMetrics(
                _currentFps,
                _lastFrameDurationMs,
                averageProcessingMs,
                _framesThisWindow,
                _totalFrames);

            _currentFps = 0;
            _lastFrameDurationMs = 0;
            _framesThisWindow = 0;
            _completedFrames = 0;
            _totalProcessingTicks = 0;
            _windowStart = _stopwatch.Elapsed;

            return metrics;
        }
    }

    private async Task RunAsync(Func<CancellationToken, Task> frameProducer, CancellationToken cancellationToken)
    {
        await Task.Yield();

        while (!cancellationToken.IsCancellationRequested)
        {
            if (!TryBeginFrame(out var frameStartTimestamp))
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
    public PreviewFrameSchedulerMetrics(
        double currentFps,
        double lastFrameDurationMs,
        double averageProcessingMilliseconds,
        long frames,
        long totalFrames)
    {
        CurrentFps = currentFps;
        LastFrameDurationMs = lastFrameDurationMs;
        AverageProcessingMilliseconds = averageProcessingMilliseconds;
        Frames = frames;
        TotalFrames = totalFrames;
    }

    public double CurrentFps { get; }

    public double LastFrameDurationMs { get; }

    public double AverageProcessingMilliseconds { get; }

    public long Frames { get; }

    public long TotalFrames { get; }
}
