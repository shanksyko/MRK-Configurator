using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Mieruka.Core.Models;
using Mieruka.Core.Diagnostics;
using Serilog;

namespace Mieruka.Preview;

/// <summary>
/// Fallback monitor capture implementation using GDI APIs.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GdiMonitorCaptureProvider : IMonitorCapture
{
    private const int TargetFramesPerSecond = 30;

    private static readonly ILogger Logger = Log.ForContext<GdiMonitorCaptureProvider>();

    private CancellationTokenSource? _cts;
    private Task? _captureLoop;
    private MonitorUtilities.RECT _monitorBounds;
    private bool _initialized;
    private readonly Stopwatch _captureStopwatch = new();
    private long _frames;
    private DateTime _lastStatsSampleUtc = DateTime.UtcNow;
    private ILogger? _captureLogger;
    private string? _captureId;
    private string? _monitorId;

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

        _monitorId = monitor.Id ?? monitor.DeviceName ?? string.Empty;
        _captureId = Guid.NewGuid().ToString("N");
        _captureLogger = Logger.ForMonitor(_monitorId).ForContext("CaptureId", _captureId);
        _captureStopwatch.Restart();
        Interlocked.Exchange(ref _frames, 0);
        _lastStatsSampleUtc = DateTime.UtcNow;
        _captureLogger.Information("PreviewStart: backend={Backend}", "GDI");

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
            CompleteSession(forceStats: false);
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
            CompleteSession(forceStats: true);
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

        RecordFrameProduced();
        handler(this, new MonitorFrameArrivedEventArgs(bitmap, DateTimeOffset.UtcNow));
    }

    private void RecordFrameProduced()
    {
        Interlocked.Increment(ref _frames);
        MaybePublishStats();
    }

    private void MaybePublishStats(bool force = false)
    {
        var now = DateTime.UtcNow;
        if (!force && (now - _lastStatsSampleUtc).TotalSeconds < 5)
        {
            return;
        }

        var frames = Interlocked.Exchange(ref _frames, 0);
        if (!force && frames == 0)
        {
            return;
        }

        var elapsedSeconds = Math.Max(0.001, (now - _lastStatsSampleUtc).TotalSeconds);
        _lastStatsSampleUtc = now;

        var logger = _captureLogger ?? Logger;
        var fps = frames / elapsedSeconds;
        logger.Debug(
            "PreviewStats: fps={Fps:F1}, frames={Frames}, dropped={Dropped}, invalid={Invalid}",
            fps,
            frames,
            0,
            0);
    }

    private void CompleteSession(bool forceStats)
    {
        var hasCapture = _captureLogger is not null || _captureStopwatch.IsRunning;
        if (!hasCapture)
        {
            Interlocked.Exchange(ref _frames, 0);
            return;
        }

        MaybePublishStats(force: forceStats);

        if (_captureStopwatch.IsRunning)
        {
            _captureStopwatch.Stop();
            var logger = _captureLogger ?? Logger;
            logger.Information("PreviewStop: uptimeMs={Uptime}", _captureStopwatch.ElapsedMilliseconds);
            _captureStopwatch.Reset();
        }

        _captureLogger = null;
        _captureId = null;
        _monitorId = null;
    }

}
