using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Mieruka.Core.Models;

namespace Mieruka.Preview;

/// <summary>
/// Wraps two monitor capture providers, falling back to the secondary implementation when necessary.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ResilientMonitorCapture : IMonitorCapture
{
    private readonly IMonitorCapture _primary;
    private readonly IMonitorCapture _fallback;
    private IMonitorCapture? _active;

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

        if (_primary.IsSupported)
        {
            try
            {
                await _primary.StartAsync(monitor, cancellationToken).ConfigureAwait(false);
                _active = _primary;
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
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

    private void OnFrameArrived(object? sender, MonitorFrameArrivedEventArgs e)
    {
        if (!ReferenceEquals(sender, _active))
        {
            return;
        }

        FrameArrived?.Invoke(this, e);
    }
}
