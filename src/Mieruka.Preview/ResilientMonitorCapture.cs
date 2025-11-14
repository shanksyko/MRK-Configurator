using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Mieruka.Core.Models;
using Serilog;

namespace Mieruka.Preview;

/// <summary>
/// Wraps two monitor capture providers, falling back to the secondary implementation when necessary.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ResilientMonitorCapture : IMonitorCapture
{
    private static readonly ILogger Logger = Log.ForContext<ResilientMonitorCapture>();
    private static readonly TimeSpan GraphicsFailureBackoff = TimeSpan.FromSeconds(60);

    private readonly IMonitorCapture _primary;
    private readonly IMonitorCapture _fallback;
    private IMonitorCapture? _active;
    private readonly object _stateGate = new();
    private readonly Dictionary<string, DateTimeOffset> _graphicsBackoff = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _loggedFallbacks = new(StringComparer.OrdinalIgnoreCase);

    public ResilientMonitorCapture(IMonitorCapture primary, IMonitorCapture fallback)
    {
        _primary = primary ?? throw new ArgumentNullException(nameof(primary));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));

        _primary.FrameArrived += OnFrameArrived;
        _fallback.FrameArrived += OnFrameArrived;
    }

    public event EventHandler<MonitorFrameArrivedEventArgs>? FrameArrived;

    public bool IsSupported => _primary.IsSupported || _fallback.IsSupported;

    public async Task StartAsync(MonitorInfo monitor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        var monitorKey = ResolveMonitorKey(monitor);
        var utcNow = DateTimeOffset.UtcNow;

        if (_primary.IsSupported && !IsGraphicsCaptureSuppressed(monitorKey, utcNow))
        {
            try
            {
                await _primary.StartAsync(monitor, cancellationToken).ConfigureAwait(false);
                _active = _primary;
                ClearGraphicsCaptureSuppression(monitorKey);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ScheduleGraphicsCaptureSuppression(monitorKey, utcNow, ex);
                await SafeStopAsync(_primary).ConfigureAwait(false);
            }
        }

        if (!_fallback.IsSupported)
        {
            throw new PlatformNotSupportedException("No supported monitor capture providers are available on this platform.");
        }

        await _fallback.StartAsync(monitor, cancellationToken).ConfigureAwait(false);
        _active = _fallback;
    }

    public ValueTask StopAsync()
    {
        var active = Interlocked.Exchange(ref _active, null);
        return active?.StopAsync() ?? ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);

        _primary.FrameArrived -= OnFrameArrived;
        _fallback.FrameArrived -= OnFrameArrived;

        await _primary.DisposeAsync().ConfigureAwait(false);
        if (!ReferenceEquals(_primary, _fallback))
        {
            await _fallback.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task SafeStopAsync(IMonitorCapture capture)
    {
        try
        {
            await capture.StopAsync().ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static string ResolveMonitorKey(MonitorInfo monitor)
    {
        if (!string.IsNullOrWhiteSpace(monitor.StableId))
        {
            return monitor.StableId;
        }

        if (!string.IsNullOrWhiteSpace(monitor.DeviceName))
        {
            return monitor.DeviceName;
        }

        if (!string.IsNullOrWhiteSpace(monitor.Key.DeviceId))
        {
            return monitor.Key.DeviceId;
        }

        return $"{monitor.Key.AdapterLuidHigh:X8}{monitor.Key.AdapterLuidLow:X8}:{monitor.Key.TargetId}";
    }

    private bool IsGraphicsCaptureSuppressed(string monitorKey, DateTimeOffset utcNow)
    {
        lock (_stateGate)
        {
            if (_graphicsBackoff.TryGetValue(monitorKey, out var until))
            {
                if (until > utcNow)
                {
                    return true;
                }

                _graphicsBackoff.Remove(monitorKey);
                _loggedFallbacks.Remove(monitorKey);
            }

            return false;
        }
    }

    private void ScheduleGraphicsCaptureSuppression(string monitorKey, DateTimeOffset utcNow, Exception exception)
    {
        lock (_stateGate)
        {
            _graphicsBackoff[monitorKey] = utcNow.Add(GraphicsFailureBackoff);
            if (_loggedFallbacks.Add(monitorKey))
            {
                Logger.Warning(
                    exception,
                    "Windows Graphics Capture falhou para '{MonitorKey}'. Usando GDI pelos próximos {Backoff}.",
                    monitorKey,
                    GraphicsFailureBackoff);
            }
        }
    }

    private void ClearGraphicsCaptureSuppression(string monitorKey)
    {
        lock (_stateGate)
        {
            if (_graphicsBackoff.Remove(monitorKey))
            {
                _loggedFallbacks.Remove(monitorKey);
            }
        }
    }

    private void OnFrameArrived(object? sender, MonitorFrameArrivedEventArgs e)
    {
        var handler = FrameArrived;

        if (!ReferenceEquals(sender, _active))
        {
            e.Dispose();
            return;
        }

        if (handler is null)
        {
            e.Dispose();
            return;
        }

        handler(this, e);
    }
}
