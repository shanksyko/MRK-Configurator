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
    private const string Backend = "GDI";

    private static readonly ILogger Logger = Log.ForContext<GdiMonitorCaptureProvider>();

    private readonly PreviewFrameScheduler _frameScheduler;
    private CancellationTokenSource? _cts;
    private MonitorUtilities.RECT _monitorBounds;
    private bool _initialized;
    private readonly Stopwatch _captureStopwatch = new();
    private long _totalFrames;
    private long _totalDropped;
    private long _totalInvalid;
    private long _sampleDropped;
    private long _sampleInvalid;
    private DateTime _lastStatsSampleUtc = DateTime.UtcNow;
    private ILogger? _captureLogger;
    private string? _previewSessionId; // Keep this unique to avoid duplicate session tracking fields
    private string? _monitorId;
    private bool _isRunning;
    private PreviewResolution? _previewResolution;

    public GdiMonitorCaptureProvider()
        : this(new PreviewFrameScheduler(PreviewSettings.Default.TargetFpsGdi))
    {
    }

    public GdiMonitorCaptureProvider(PreviewFrameScheduler frameScheduler)
    {
        _frameScheduler = frameScheduler ?? throw new ArgumentNullException(nameof(frameScheduler));
    }

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

        if (_cts is { IsCancellationRequested: false })
        {
            throw new InvalidOperationException("The capture session is already running.");
        }

        if (!MonitorUtilities.TryGetMonitorHandle(monitor.DeviceName, out _, out var bounds))
        {
            throw new InvalidOperationException($"Unable to locate monitor '{monitor.DeviceName}'.");
        }

        _monitorBounds = bounds;
        _initialized = true;
        _previewResolution = monitor.GetPreviewResolution();

        _monitorId = monitor.Id ?? monitor.DeviceName ?? string.Empty;
        _previewSessionId = Guid.NewGuid().ToString("N");
        _captureLogger = Logger
            .ForMonitor(_monitorId)
            .ForContext("MonitorId", _monitorId)
            .ForContext("Backend", Backend)
            .ForContext("PreviewSessionId", _previewSessionId);
        _captureStopwatch.Restart();
        Interlocked.Exchange(ref _totalFrames, 0);
        Interlocked.Exchange(ref _totalDropped, 0);
        Interlocked.Exchange(ref _totalInvalid, 0);
        _sampleDropped = 0;
        _sampleInvalid = 0;
        _lastStatsSampleUtc = DateTime.UtcNow;
        _captureLogger.Information(
            "PreviewStart: backend={Backend}, monitorId={MonitorId}, previewSessionId={PreviewSessionId}, targetFps={TargetFps}",
            Backend,
            _monitorId,
            _previewSessionId,
            _frameScheduler.TargetFps);

        _isRunning = true;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _frameScheduler.Start(CaptureFrameAsync, _cts.Token);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask StopAsync()
    {
        var hasActiveSession = _isRunning;
        _isRunning = false;

        if (_cts is null)
        {
            _initialized = false;
            CompleteSession(forceStats: false, hasActiveSession: hasActiveSession);
            return;
        }

        try
        {
            _cts.Cancel();
            await _frameScheduler.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            _initialized = false;
            _cts.Dispose();
            _cts = null;
            CompleteSession(forceStats: hasActiveSession, hasActiveSession: hasActiveSession);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private Task CaptureFrameAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var bitmap = CaptureFrame();

        if (cancellationToken.IsCancellationRequested)
        {
            bitmap.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
        }

        DispatchFrame(bitmap);
        return Task.CompletedTask;
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

        var target = _previewResolution;
        if (target is { HasValidSize: true })
        {
            return ScreenCapture.CaptureRectangle(rectangle, target.ToSize());
        }

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
        if (!_isRunning || _cts?.IsCancellationRequested == true)
        {
            return;
        }

        Interlocked.Increment(ref _totalFrames);
        MaybePublishStats();
    }

    private void MaybePublishStats(bool force = false)
    {
        if (!_isRunning && !force)
        {
            return;
        }

        if (_cts?.IsCancellationRequested == true && !force)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (!force && (now - _lastStatsSampleUtc).TotalSeconds < 5)
        {
            return;
        }

        var schedulerMetrics = _frameScheduler.GetMetricsAndReset();
        var dropped = Interlocked.Exchange(ref _sampleDropped, 0);
        var invalid = Interlocked.Exchange(ref _sampleInvalid, 0);

        if (!force && schedulerMetrics.Frames == 0 && dropped == 0 && invalid == 0)
        {
            return;
        }

        var elapsedSeconds = Math.Max(0.001, (now - _lastStatsSampleUtc).TotalSeconds);
        _lastStatsSampleUtc = now;

        var fps = schedulerMetrics.Frames / elapsedSeconds;
        var effectiveFps = schedulerMetrics.EffectiveFps;
        var effectiveIntervalMs = schedulerMetrics.EffectiveFrameInterval.TotalMilliseconds;
        var logger = _captureLogger ?? Logger;
        logger.Debug(
            "PreviewStats: backend={Backend}, monitorId={MonitorId}, previewSessionId={PreviewSessionId}, targetFps={TargetFps}, effectiveFps={EffectiveFps:F1}, effectiveIntervalMs={EffectiveIntervalMs:F2}, fps={Fps:F1}, frameProcessingAvgMs={FrameProcessingAvgMs:F2}, frames={Frames}, dropped={Dropped}, invalid={Invalid}, totalFrames={TotalFrames}, totalDropped={TotalDropped}, totalInvalid={TotalInvalid}",
            Backend,
            _monitorId,
            _previewSessionId,
            _frameScheduler.TargetFps,
            effectiveFps,
            effectiveIntervalMs,
            fps,
            schedulerMetrics.AverageProcessingMilliseconds,
            schedulerMetrics.Frames,
            dropped,
            invalid,
            Interlocked.Read(ref _totalFrames),
            Interlocked.Read(ref _totalDropped),
            Interlocked.Read(ref _totalInvalid));
    }

    private void CompleteSession(bool forceStats, bool hasActiveSession)
    {
        var hasCapture = hasActiveSession && (_captureLogger is not null || _captureStopwatch.IsRunning);
        if (!hasCapture)
        {
            return;
        }

        MaybePublishStats(force: forceStats);

        var totalFrames = Interlocked.Read(ref _totalFrames);
        var totalDropped = Interlocked.Read(ref _totalDropped);
        var totalInvalid = Interlocked.Read(ref _totalInvalid);

        if (_captureStopwatch.IsRunning)
        {
            _captureStopwatch.Stop();
            var logger = _captureLogger ?? Logger;
            logger.Information(
                "PreviewStop: backend={Backend}, monitorId={MonitorId}, previewSessionId={PreviewSessionId}, uptimeMs={Uptime}, frames={Frames}, dropped={Dropped}, invalid={Invalid}",
                Backend,
                _monitorId,
                _previewSessionId,
                _captureStopwatch.ElapsedMilliseconds,
                totalFrames,
                totalDropped,
                totalInvalid);
            _captureStopwatch.Reset();
        }

        _captureLogger = null;
        _previewSessionId = null;
        _monitorId = null;
        Interlocked.Exchange(ref _totalFrames, 0);
        Interlocked.Exchange(ref _totalDropped, 0);
        Interlocked.Exchange(ref _totalInvalid, 0);
        _sampleDropped = 0;
        _sampleInvalid = 0;
    }

}
