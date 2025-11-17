using System;
using System.Diagnostics;

namespace Mieruka.Preview;

public sealed class PreviewFrameScheduler
{
    private readonly object _gate = new();
    private readonly Stopwatch _stopwatch = new();
    private readonly long _targetFrameTicks;

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
