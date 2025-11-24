using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Mieruka.Preview;

public sealed class PreviewFrameScheduler
{
    private readonly object _gate = new();
    private readonly Stopwatch _stopwatch = new();
    private readonly TimeSpan _targetFrameInterval;
    private TimeSpan _effectiveFrameInterval;
    private const double MinEffectiveFps = 10d;
    private const double OverBudgetTolerance = 1.05d;
    private const double UnderBudgetTolerance = 0.9d;
    private const double AdjustmentSmoothing = 0.25d;

    private CancellationTokenSource? _cts;
    private Task? _worker;

    private long _nextFrameAtTicks;
    private long _framesThisWindow;
    private TimeSpan _windowStart;
    private double _currentFps;
    private double _effectiveFps;
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
        _targetFrameInterval = TimeSpan.FromSeconds(1d / TargetFps);
        _effectiveFrameInterval = _targetFrameInterval;
        _currentFps = TargetFps;
        _windowStart = TimeSpan.Zero;

        _stopwatch.Start();
    }

    public double TargetFps { get; }

    public double EffectiveFps
    {
        get
        {
            lock (_gate)
            {
                return _effectiveFps;
            }
        }
    }

    public TimeSpan EffectiveFrameInterval
    {
        get
        {
            lock (_gate)
            {
                return _effectiveFrameInterval;
            }
        }
    }

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
            _effectiveFps = TargetFps;
            _effectiveFrameInterval = _targetFrameInterval;

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

            _nextFrameAtTicks = now.Ticks + _effectiveFrameInterval.Ticks;
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
            var averageProcessingMs = _completedFrames == 0
                ? 0
                : (_totalProcessingTicks / (double)_completedFrames) / TimeSpan.TicksPerMillisecond;

            var now = TimeSpan.FromTicks(nowTicks);
            var windowElapsed = now - _windowStart;
            if (windowElapsed.TotalSeconds >= 1)
            {
                _currentFps = _framesThisWindow / windowElapsed.TotalSeconds;
                _framesThisWindow = 0;
                _windowStart = now;
            }

            AdjustEffectiveFrameRate(averageProcessingMs);
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
                _totalFrames,
                _effectiveFps,
                _effectiveFrameInterval);

            _currentFps = 0;
            _lastFrameDurationMs = 0;
            _framesThisWindow = 0;
            _completedFrames = 0;
            _totalProcessingTicks = 0;
            _windowStart = _stopwatch.Elapsed;

            return metrics;
        }
    }

    private void AdjustEffectiveFrameRate(double averageProcessingMs)
    {
        if (double.IsNaN(averageProcessingMs) || double.IsInfinity(averageProcessingMs))
        {
            return;
        }

        var targetIntervalMs = _targetFrameInterval.TotalMilliseconds;
        if (targetIntervalMs <= 0)
        {
            return;
        }

        var desiredFps = TargetFps;

        if (averageProcessingMs > targetIntervalMs * OverBudgetTolerance)
        {
            var budgetRatio = targetIntervalMs / Math.Max(1d, averageProcessingMs);
            desiredFps = Math.Max(MinEffectiveFps, TargetFps * budgetRatio);
        }
        else if (averageProcessingMs < targetIntervalMs * UnderBudgetTolerance && _effectiveFps < TargetFps)
        {
            desiredFps = TargetFps;
        }
        else
        {
            desiredFps = Math.Min(TargetFps, _effectiveFps);
        }

        var smoothedFps = _effectiveFps == 0 ? desiredFps : Lerp(_effectiveFps, desiredFps, AdjustmentSmoothing);
        smoothedFps = Math.Clamp(smoothedFps, MinEffectiveFps, TargetFps);

        _effectiveFps = smoothedFps;
        _effectiveFrameInterval = TimeSpan.FromSeconds(1d / _effectiveFps);
    }

    private static double Lerp(double from, double to, double weight) => from + (to - from) * weight;

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
        long totalFrames,
        double effectiveFps,
        TimeSpan effectiveFrameInterval)
    {
        CurrentFps = currentFps;
        LastFrameDurationMs = lastFrameDurationMs;
        AverageProcessingMilliseconds = averageProcessingMilliseconds;
        Frames = frames;
        TotalFrames = totalFrames;
        EffectiveFps = effectiveFps;
        EffectiveFrameInterval = effectiveFrameInterval;
    }

    public double CurrentFps { get; }

    public double LastFrameDurationMs { get; }

    public double AverageProcessingMilliseconds { get; }

    public long Frames { get; }

    public long TotalFrames { get; }

    public double EffectiveFps { get; }

    public TimeSpan EffectiveFrameInterval { get; }
}
