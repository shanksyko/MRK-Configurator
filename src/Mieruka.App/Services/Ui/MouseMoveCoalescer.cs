using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace Mieruka.App.Services.Ui;

internal sealed class MouseMoveCoalescer : IMessageFilter
{
    private const int WM_MOUSEMOVE = 0x0200;
    private readonly long _debounceTicks;
    private long _lastDeliveredTimestamp;

    public MouseMoveCoalescer(TimeSpan debounceInterval)
    {
        if (debounceInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(debounceInterval));
        }

        var ticks = debounceInterval.TotalSeconds * Stopwatch.Frequency;
        _debounceTicks = ticks <= 1 ? 1 : (long)Math.Round(ticks);
    }

    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg != WM_MOUSEMOVE)
        {
            return false;
        }

        var now = Stopwatch.GetTimestamp();

        if (_lastDeliveredTimestamp == 0 || now - _lastDeliveredTimestamp >= _debounceTicks)
        {
            _lastDeliveredTimestamp = now;
            return false;
        }

        return true;
    }
}
