using System;
using System.Drawing;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Mieruka.Core.Models;

namespace Mieruka.Preview;

/// <summary>
/// Fallback monitor capture implementation using GDI APIs.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GdiMonitorCaptureProvider : IMonitorCapture
{
    private const int TargetFramesPerSecond = 30;

    private CancellationTokenSource? _cts;
    private Task? _captureLoop;
    private MonitorUtilities.RECT _monitorBounds;
    private bool _initialized;

    /// <inheritdoc />
    public event EventHandler<MonitorFrameArrivedEventArgs>? FrameArrived;

    /// <inheritdoc />
    public bool IsSupported => OperatingSystem.IsWindows();

    /// <inheritdoc />
    public Task StartAsync(MonitorInfo monitor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        if (monitor.DeviceName is null)
        {
            throw new ArgumentException("Monitor device name is not defined.", nameof(monitor));
        }

        if (_captureLoop is { IsCompleted: false })
        {
            throw new InvalidOperationException("The capture session is already running.");
        }

        if (!MonitorUtilities.TryGetMonitorHandle(monitor.DeviceName, out _, out var bounds))
        {
            throw new InvalidOperationException($"Unable to locate monitor '{monitor.DeviceName}'.");
        }

        _monitorBounds = bounds;
        _initialized = true;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _captureLoop = Task.Run(() => CaptureLoopAsync(_cts.Token), CancellationToken.None);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask StopAsync()
    {
        if (_cts is null)
        {
            _initialized = false;
            return;
        }

        try
        {
            _cts.Cancel();
            if (_captureLoop is not null)
            {
                await _captureLoop.ConfigureAwait(false);
            }
        }
        finally
        {
            _captureLoop = null;
            _initialized = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private async Task CaptureLoopAsync(CancellationToken cancellationToken)
    {
        var frameInterval = TimeSpan.FromMilliseconds(1000.0 / TargetFramesPerSecond);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var bitmap = CaptureFrame();
                DispatchFrame(bitmap);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Swallow unexpected exceptions to keep the capture loop alive.
            }

            try
            {
                await Task.Delay(frameInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private Bitmap CaptureFrame()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("The capture provider has not been initialized.");
        }

        var width = _monitorBounds.Width;
        var height = _monitorBounds.Height;

        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("The monitor dimensions are invalid.");
        }

        var rectangle = new Rectangle(
            _monitorBounds.Left,
            _monitorBounds.Top,
            width,
            height);

        return ScreenCapture.CaptureRectangle(rectangle);
    }

    private void DispatchFrame(Bitmap bitmap)
    {
        var handler = FrameArrived;
        if (handler is null)
        {
            bitmap.Dispose();
            return;
        }

        handler(this, new MonitorFrameArrivedEventArgs(bitmap, DateTimeOffset.UtcNow));
    }

}
