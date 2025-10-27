using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace Mieruka.App.Services.Ui;

internal sealed class MouseMoveCoalescer : IMessageFilter
{
    private const int WmMouseMove = 0x0200;
    private readonly long _minimumIntervalTicks;
    private long _lastDeliveredTimestamp;

    public MouseMoveCoalescer(int minimumIntervalMilliseconds)
        : this(TimeSpan.FromMilliseconds(minimumIntervalMilliseconds))
    {
    }

    public MouseMoveCoalescer(TimeSpan minimumInterval)
    {
        if (minimumInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumInterval));
        }

        var ticks = minimumInterval.TotalSeconds * Stopwatch.Frequency;
        _minimumIntervalTicks = ticks <= 1 ? 1 : (long)Math.Round(ticks);
    }

    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg != WmMouseMove)
        {
            return false;
        }

        var now = Stopwatch.GetTimestamp();
        var previous = Volatile.Read(ref _lastDeliveredTimestamp);

        if (previous == 0 || now - previous >= _minimumIntervalTicks)
        {
            Volatile.Write(ref _lastDeliveredTimestamp, now);
            return false;
        }

        return true;
    }
}
