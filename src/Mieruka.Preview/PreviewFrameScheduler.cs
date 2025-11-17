using System;
using System.Diagnostics;
using System.Threading;

namespace Mieruka.Preview;

public sealed class PreviewFrameScheduler
{
    private readonly long _targetFrameTicks;
    private long _lastFrameTimestamp;
    private int _inFlight;

    public PreviewFrameScheduler()
        : this(PreviewFrameSchedulerOptions.Default)
    {
    }

    public PreviewFrameScheduler(PreviewFrameSchedulerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _targetFrameTicks = (long)Math.Round(TimeSpan.TicksPerSecond / options.FramesPerSecond);
    }

    public bool TryBeginFrame(out TimeSpan waitTime)
    {
        var now = Stopwatch.GetTimestamp();
        var last = Volatile.Read(ref _lastFrameTimestamp);

        if (last != 0)
        {
            var elapsedTicks = (now - last) * TimeSpan.TicksPerSecond / Stopwatch.Frequency;
            if (elapsedTicks < _targetFrameTicks)
            {
                waitTime = TimeSpan.FromTicks(Math.Max(0, _targetFrameTicks - elapsedTicks));
                return false;
            }
        }

        if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0)
        {
            waitTime = TimeSpan.Zero;
            return false;
        }

        waitTime = TimeSpan.Zero;
        return true;
    }

    public void EndFrame()
    {
        if (Interlocked.Exchange(ref _inFlight, 0) == 0)
        {
            return;
        }

        Volatile.Write(ref _lastFrameTimestamp, Stopwatch.GetTimestamp());
    }
}
