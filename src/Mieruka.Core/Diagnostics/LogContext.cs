using System;
using System.Collections.Generic;
using Serilog;

namespace Mieruka.Core.Diagnostics;

public static class LogContextEx
{
    public static ILogger ForSession(Guid sessionId) =>
        Log.ForContext("SessionId", sessionId);

    public static ILogger ForMonitor(this ILogger log, string monitorId) =>
        log.ForContext("MonitorId", monitorId);

    public static ILogger ForCapture(this ILogger log, string captureId) =>
        log.ForContext("CaptureId", captureId);

    public static IDisposable PushCorrelation(Guid sessionId, string? monitorId = null, string? captureId = null)
    {
        var disposables = new List<IDisposable>
        {
            Serilog.Context.LogContext.PushProperty("SessionId", sessionId)
        };

        if (monitorId is not null)
        {
            disposables.Add(Serilog.Context.LogContext.PushProperty("MonitorId", monitorId));
        }

        if (captureId is not null)
        {
            disposables.Add(Serilog.Context.LogContext.PushProperty("CaptureId", captureId));
        }

        return new CompositeDisposable(disposables);
    }

    private sealed class CompositeDisposable : IDisposable
    {
        private readonly IReadOnlyList<IDisposable> _items;

        public CompositeDisposable(IReadOnlyList<IDisposable> items)
        {
            _items = items;
        }

        public void Dispose()
        {
            for (var i = _items.Count - 1; i >= 0; i--)
            {
                _items[i].Dispose();
            }
        }
    }
}
