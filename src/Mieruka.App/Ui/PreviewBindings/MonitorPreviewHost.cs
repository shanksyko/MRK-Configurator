using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Mieruka.Core.Models;
using Mieruka.Core.Monitors;
using Mieruka.Preview;
using Serilog;

namespace Mieruka.App.Ui.PreviewBindings;

/// <summary>
/// Hosts a monitor preview session and binds frames to a <see cref="PictureBox"/>.
/// </summary>
public sealed class MonitorPreviewHost : IDisposable
{
    private readonly PictureBox _target;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private readonly object _frameTimingGate = new();
    private readonly object _stateGate = new();
    private readonly object _pendingFramesGate = new();
    private readonly HashSet<Bitmap> _pendingFrames = new();
    private TimeSpan _frameThrottle = TimeSpan.FromMilliseconds(300);
    private Bitmap? _currentFrame;
    private bool _disposed;
    private Rectangle _monitorBounds;
    private Rectangle _monitorWorkArea;
    private MonitorOrientation _orientation;
    private int _rotation;
    private int _refreshRate;
    private DateTime _nextFrameAt;
    private bool _hasActiveSession;
    private bool _lastPreferGpu;
    private volatile bool _isSuspended;
    private int _resumeTicket;
    private int _paused;
    private int _busy;
    private int _frameCallbackGate;

    public MonitorPreviewHost(string monitorId, PictureBox target, ILogger? logger = null)
    {
        MonitorId = monitorId ?? throw new ArgumentNullException(nameof(monitorId));
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _logger = (logger ?? Log.ForContext<MonitorPreviewHost>()).ForContext("MonitorId", MonitorId);
        EnsurePictureBoxSizeMode();
    }

    public MonitorPreviewHost(MonitorDescriptor descriptor, PictureBox target, ILogger? logger = null)
        : this(CreateMonitorId(descriptor), target, logger)
    {
        _monitorBounds = descriptor.Bounds;
        _monitorWorkArea = descriptor.WorkArea;
        _orientation = descriptor.Orientation;
        _rotation = descriptor.Rotation;
        _refreshRate = descriptor.RefreshHz;
    }

    /// <summary>
    /// Gets the identifier of the monitor being previewed.
    /// </summary>
    public string MonitorId { get; }

    /// <summary>
    /// Gets the active capture session, if any.
    /// </summary>
    public IMonitorCapture? Capture { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the host is currently executing a mutating operation.
    /// </summary>
    public bool IsBusy => Volatile.Read(ref _busy) != 0;

    /// <summary>
    /// Gets a value indicating whether the host is currently paused.
    /// </summary>
    public bool IsPaused => Volatile.Read(ref _paused) == 1;

    /// <summary>
    /// Gets the bounds of the monitor being captured when available.
    /// </summary>
    public Rectangle MonitorBounds => _monitorBounds;

    /// <summary>
    /// Gets the work area of the monitor being captured when available.
    /// </summary>
    public Rectangle MonitorWorkArea => _monitorWorkArea;

    /// <summary>
    /// Gets the orientation of the monitor when available.
    /// </summary>
    public MonitorOrientation Orientation => _orientation;

    /// <summary>
    /// Gets the rotation of the monitor in degrees when available.
    /// </summary>
    public int Rotation => _rotation;

    /// <summary>
    /// Gets or sets the minimum interval between captured frames.
    /// </summary>
    public TimeSpan FrameThrottle
    {
        get
        {
            lock (_frameTimingGate)
            {
                return _frameThrottle;
            }
        }
        set
        {
            var sanitized = value < TimeSpan.Zero ? TimeSpan.Zero : value;
            lock (_frameTimingGate)
            {
                _frameThrottle = sanitized;
                if (_frameThrottle <= TimeSpan.Zero)
                {
                    _nextFrameAt = DateTime.MinValue;
                }
            }
        }
    }

    /// <summary>
    /// Starts the preview using GPU capture when available.
    /// </summary>
    public bool Start(bool preferGpu)
    {
        if (!TryEnterBusy(out var scope))
        {
            LogReentrancyBlocked(nameof(Start));
            return Capture is not null;
        }

        try
        {
            return StartCore(preferGpu);
        }
        finally
        {
            scope.Dispose();
        }
    }

    /// <summary>
    /// Stops the current preview session.
    /// </summary>
    public void Stop()
    {
        if (!TryEnterBusy(out var scope))
        {
            LogReentrancyBlocked(nameof(Stop));
            return;
        }

        try
        {
            lock (_stateGate)
            {
                _hasActiveSession = false;
                _isSuspended = false;
            }

            Interlocked.Exchange(ref _paused, 0);
            StopCaptureCore(clearFrame: true);
        }
        finally
        {
            scope.Dispose();
        }
    }

    /// <summary>
    /// Temporarily suspends the capture pipeline while keeping the current frame.
    /// </summary>
    public void SuspendCapture()
    {
        if (_disposed)
        {
            return;
        }

        if (!TryEnterBusy(out var scope))
        {
            LogReentrancyBlocked(nameof(SuspendCapture));
            return;
        }

        try
        {
            var shouldSuspend = false;

            lock (_stateGate)
            {
                if (_hasActiveSession && !_isSuspended)
                {
                    _isSuspended = true;
                    shouldSuspend = true;
                }
            }

            if (!shouldSuspend)
            {
                return;
            }

            StopCaptureCore(clearFrame: false);
        }
        finally
        {
            scope.Dispose();
        }
    }

    /// <summary>
    /// Resumes a previously suspended capture session.
    /// </summary>
    public void ResumeCapture()
    {
        if (_disposed)
        {
            return;
        }

        if (!TryEnterBusy(out var scope))
        {
            return;
        }

        int ticket;
        bool shouldResume;
        bool preferGpu;

        try
        {
            ticket = Interlocked.Increment(ref _resumeTicket);

            lock (_stateGate)
            {
                shouldResume = _hasActiveSession && _isSuspended;
                preferGpu = _lastPreferGpu;
            }

            if (!shouldResume)
            {
                return;
            }
        }
        finally
        {
            scope.Dispose();
        }

        void ResumeCore()
        {
            if (!TryEnterBusy(out var resumeScope))
            {
                LogReentrancyBlocked(nameof(ResumeCapture));
                return;
            }

            try
            {
                if (_disposed || _target.IsDisposed || ticket != Volatile.Read(ref _resumeTicket))
                {
                    return;
                }

                bool resumeNow;
                lock (_stateGate)
                {
                    resumeNow = _hasActiveSession && _isSuspended && ticket == _resumeTicket;
                    if (resumeNow)
                    {
                        _isSuspended = false;
                    }
                }

                if (!resumeNow)
                {
                    return;
                }

                StartCore(preferGpu);
            }
            finally
            {
                resumeScope.Dispose();
            }
        }

        if (_target.InvokeRequired)
        {
            try
            {
                _target.BeginInvoke(new Action(ResumeCore));
            }
            catch
            {
                // Ignore invoke failures during shutdown.
            }

            return;
        }

        ResumeCore();
    }

    public void Pause()
    {
        if (_disposed)
        {
            return;
        }

        if (!TryEnterBusy(out var scope))
        {
            return;
        }

        try
        {
            var wasPaused = Interlocked.Exchange(ref _paused, 1) == 1;
            var detached = DisposeCaptureRetainingFrame(resetPaused: false);

            if (!wasPaused || detached)
            {
                _logger.Information("PreviewPaused");
            }
        }
        finally
        {
            scope.Dispose();
        }
    }

    public void Resume()
    {
        if (_disposed)
        {
            return;
        }

        if (!TryEnterBusy(out var scope))
        {
            return;
        }

        try
        {
            if (Volatile.Read(ref _paused) == 0)
            {
                return;
            }

            bool preferGpu;
            lock (_stateGate)
            {
                preferGpu = _lastPreferGpu;
            }

            if (StartCore(preferGpu))
            {
                _logger.Information("PreviewResumed");
            }
        }
        finally
        {
            scope.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }

    private bool TryEnterBusy(out BusyScope scope)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            scope = default;
            return false;
        }

        scope = new BusyScope(this);
        return true;
    }

    private void ExitBusy()
    {
        Interlocked.Exchange(ref _busy, 0);
    }

    private IEnumerable<(string Mode, Func<IMonitorCapture> Factory)> EnumerateFactories(bool preferGpu)
    {
        if (preferGpu)
        {
            yield return ("GPU", () => CreateForMonitor.Gpu(MonitorId));
            yield return ("GDI", () => CreateForMonitor.Gdi(MonitorId));
        }
        else
        {
            yield return ("GDI", () => CreateForMonitor.Gdi(MonitorId));
            yield return ("GPU", () => CreateForMonitor.Gpu(MonitorId));
        }
    }

    private bool StartCore(bool preferGpu)
    {
        if (_disposed)
        {
            return false;
        }

        if (Capture is not null)
        {
            return true;
        }

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        if (!PopulateMetadataFromMonitor())
        {
            return false;
        }

        foreach (var (mode, factory) in EnumerateFactories(preferGpu))
        {
            IMonitorCapture? capture = null;
            try
            {
                capture = factory();
                capture.FrameArrived += OnFrameArrived;

                lock (_gate)
                {
                    Capture = capture;
                    Interlocked.Exchange(ref _paused, 0);
                }

                lock (_stateGate)
                {
                    _hasActiveSession = true;
                    _lastPreferGpu = preferGpu;
                    _isSuspended = false;
                }

                return true;
            }
            catch (Exception ex)
            {
                if (preferGpu && string.Equals(mode, "GPU", StringComparison.OrdinalIgnoreCase))
                {
                    if (ex is NotSupportedException)
                    {
                        ForEvent("MonitorFallback")
                            .Warning(ex, "Captura via GPU não suportada para {MonitorId}; usando fallback GDI.", MonitorId);
                    }
                    else if (ex is ArgumentException)
                    {
                        ForEvent("MonitorFallback")
                            .Warning(ex, "Falha ao iniciar captura via GPU para {MonitorId}; tentando fallback.", MonitorId);
                    }
                    else
                    {
                        ForEvent("MonitorFallback")
                            .Warning(ex, "Falha inesperada ao iniciar captura via GPU para {MonitorId}; fallback será usado.", MonitorId);
                    }
                }
                else
                {
                    _logger.Warning(ex, "Falha ao iniciar captura ({Mode}) para {MonitorId}.", mode, MonitorId);
                }
                if (capture is not null)
                {
                    capture.FrameArrived -= OnFrameArrived;
                    SafeDispose(capture);
                }
            }
        }

        lock (_stateGate)
        {
            if (!_isSuspended)
            {
                _hasActiveSession = false;
            }
        }

        return false;
    }

    private void OnFrameArrived(object? sender, MonitorFrameArrivedEventArgs e)
    {
        if (Interlocked.Exchange(ref _frameCallbackGate, 1) != 0)
        {
            _logger.Information("ReentrancyBlocked");
            e.Dispose();
            return;
        }

        try
        {
            if (Volatile.Read(ref _paused) == 1)
            {
                _logger.Information("FrameDroppedPaused");
                e.Dispose();
                return;
            }

            if (IsBusy || !ShouldDisplayFrame())
            {
                e.Dispose();
                return;
            }

            if (!ShouldProcessFrame())
            {
                e.Dispose();
                return;
            }

            Bitmap? clone = null;
            try
            {
                var bitmap = e.Frame;
                clone = bitmap.Clone(new Rectangle(0, 0, bitmap.Width, bitmap.Height), bitmap.PixelFormat);
#if DEBUG
                DrawDebugOverlay(clone);
#endif
            }
            catch
            {
                // Ignore frame cloning issues.
            }
            finally
            {
                e.Dispose();
            }

            if (clone is null)
            {
                return;
            }

            RegisterPendingFrame(clone);
            UpdateTarget(clone);
        }
        finally
        {
            Interlocked.Exchange(ref _frameCallbackGate, 0);
        }
    }

    private void UpdateTarget(Bitmap frame)
    {
        if (_target.IsDisposed)
        {
            UnregisterPendingFrame(frame);
            frame.Dispose();
            return;
        }

        if (_target.InvokeRequired)
        {
            if (!ShouldDisplayFrame())
            {
                UnregisterPendingFrame(frame);
                frame.Dispose();
                return;
            }

            try
            {
                _target.BeginInvoke(new Action<Bitmap>(UpdateTarget), frame);
            }
            catch
            {
                UnregisterPendingFrame(frame);
                frame.Dispose();
            }

            return;
        }

        UnregisterPendingFrame(frame);

        if (!ShouldDisplayFrame())
        {
            frame.Dispose();
            return;
        }

        var previous = _currentFrame;
        _currentFrame = frame;
        _target.Image = frame;
        previous?.Dispose();
    }

    private void ClearFrame()
    {
        if (_target.IsDisposed)
        {
            _currentFrame?.Dispose();
            _currentFrame = null;
            ResetFrameThrottle();
            return;
        }

        if (_target.InvokeRequired)
        {
            try
            {
                _target.Invoke(new Action(ClearFrame));
            }
            catch
            {
                // Ignore invoke failures during shutdown.
            }

            return;
        }

        if (ReferenceEquals(_target.Image, _currentFrame))
        {
            _target.Image = null;
        }

        _currentFrame?.Dispose();
        _currentFrame = null;
        ResetFrameThrottle();
    }

    private void StopCaptureCore(bool clearFrame, bool resetPaused = true)
    {
        IMonitorCapture? capture;
        lock (_gate)
        {
            capture = Capture;
            Capture = null;
        }

        if (capture is not null)
        {
            capture.FrameArrived -= OnFrameArrived;
            SafeDispose(capture);
            _logger.Information("CaptureDisposed");
        }

        DisposePendingFrames();

        if (resetPaused)
        {
            Interlocked.Exchange(ref _paused, 0);
        }

        if (clearFrame)
        {
            ClearFrame();
        }
        else
        {
            ResetFrameThrottle();
        }
    }

    private bool DisposeCaptureRetainingFrame(bool resetPaused)
    {
        bool hasCapture;
        lock (_gate)
        {
            hasCapture = Capture is not null;
        }

        if (!hasCapture)
        {
            if (resetPaused)
            {
                Interlocked.Exchange(ref _paused, 0);
            }

            return false;
        }

        StopCaptureCore(clearFrame: false, resetPaused: resetPaused);
        return true;
    }

    private bool ShouldProcessFrame()
    {
        TimeSpan throttle;
        lock (_frameTimingGate)
        {
            throttle = _frameThrottle;
        }

        if (throttle <= TimeSpan.Zero)
        {
            return true;
        }

        var now = DateTime.UtcNow;
        lock (_frameTimingGate)
        {
            if (now < _nextFrameAt)
            {
                return false;
            }

            _nextFrameAt = now + throttle;
            return true;
        }
    }

    private void ResetFrameThrottle()
    {
        lock (_frameTimingGate)
        {
            _nextFrameAt = DateTime.MinValue;
        }
    }

    private bool ShouldDisplayFrame()
    {
        if (Volatile.Read(ref _paused) == 1 || _isSuspended)
        {
            return false;
        }

        lock (_stateGate)
        {
            return _hasActiveSession;
        }
    }

    private void RegisterPendingFrame(Bitmap frame)
    {
        lock (_pendingFramesGate)
        {
            _pendingFrames.Add(frame);
        }
    }

    private void UnregisterPendingFrame(Bitmap frame)
    {
        lock (_pendingFramesGate)
        {
            _pendingFrames.Remove(frame);
        }
    }

    private void DisposePendingFrames()
    {
        Bitmap[]? frames = null;

        lock (_pendingFramesGate)
        {
            if (_pendingFrames.Count > 0)
            {
                frames = new Bitmap[_pendingFrames.Count];
                _pendingFrames.CopyTo(frames);
                _pendingFrames.Clear();
            }
        }

        if (frames is null)
        {
            return;
        }

        foreach (var frame in frames)
        {
            try
            {
                frame.Dispose();
            }
            catch
            {
                // Ignore failures while disposing pending frames.
            }
        }
    }

    private static void SafeDispose(IMonitorCapture capture)
    {
        try
        {
            capture.StopAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Ignore stop failures.
        }

        try
        {
            capture.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch
        {
            // Ignore dispose failures.
        }
    }

    internal static string CreateMonitorId(MonitorDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var hasAdapter = descriptor.AdapterLuidHi != 0 || descriptor.AdapterLuidLo != 0 || descriptor.TargetId != 0;
        if (!hasAdapter)
        {
            return descriptor.DeviceName ?? string.Empty;
        }

        var key = new MonitorKey
        {
            AdapterLuidHigh = (int)descriptor.AdapterLuidHi,
            AdapterLuidLow = (int)descriptor.AdapterLuidLo,
            TargetId = unchecked((int)descriptor.TargetId),
            DeviceId = descriptor.DeviceName ?? string.Empty,
        };

        return MonitorIdentifier.Create(key, descriptor.DeviceName);
    }

    private ILogger ForEvent(string eventId)
        => _logger.ForContext("EventId", eventId);

    private void LogReentrancyBlocked(string operation)
        => ForEvent("ReentrancyBlocked").Debug("Operação {Operation} bloqueada por reentrância.", operation);

    private void EnsurePictureBoxSizeMode()
    {
        if (_target.SizeMode != PictureBoxSizeMode.Zoom)
        {
            _target.SizeMode = PictureBoxSizeMode.Zoom;
        }
    }

    private bool PopulateMetadataFromMonitor()
    {
        if (_monitorBounds != Rectangle.Empty)
        {
            return true;
        }

        MonitorInfo? info = null;
        try
        {
            info = MonitorLocator.Find(MonitorId);
        }
        catch
        {
            // Metadata retrieval is best-effort only.
        }

        if (info is not null)
        {
            if (info.Bounds != Rectangle.Empty)
            {
                _monitorBounds = info.Bounds;
            }

            if (info.WorkArea != Rectangle.Empty)
            {
                _monitorWorkArea = info.WorkArea;
            }

            if (info.Orientation != MonitorOrientation.Unknown)
            {
                _orientation = info.Orientation;
            }

            if (info.Rotation != 0)
            {
                _rotation = info.Rotation;
            }

            if (_monitorBounds == Rectangle.Empty && info.Width > 0 && info.Height > 0)
            {
                _monitorBounds = new Rectangle(0, 0, info.Width, info.Height);
            }

            if (_monitorWorkArea == Rectangle.Empty && _monitorBounds != Rectangle.Empty)
            {
                _monitorWorkArea = _monitorBounds;
            }

            if (_monitorBounds != Rectangle.Empty)
            {
                return true;
            }
        }

        return TryPopulateMetadataFallback();
    }

    private bool TryPopulateMetadataFallback()
    {
        try
        {
            var service = new MonitorService();
            var fallback = service.PrimaryOrFirst();

            if (fallback.Bounds != Rectangle.Empty)
            {
                _monitorBounds = fallback.Bounds;
            }
            else if (fallback.Width > 0 && fallback.Height > 0)
            {
                _monitorBounds = new Rectangle(0, 0, fallback.Width, fallback.Height);
            }

            if (fallback.WorkArea != Rectangle.Empty)
            {
                _monitorWorkArea = fallback.WorkArea;
            }
            else if (_monitorBounds != Rectangle.Empty)
            {
                _monitorWorkArea = _monitorBounds;
            }

            if (fallback.Orientation != MonitorOrientation.Unknown)
            {
                _orientation = fallback.Orientation;
            }

            if (fallback.Rotation != 0)
            {
                _rotation = fallback.Rotation;
            }

            if (fallback.RefreshHz > 0)
            {
                _refreshRate = fallback.RefreshHz;
            }

            return _monitorBounds != Rectangle.Empty;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

#if DEBUG
    private void DrawDebugOverlay(Bitmap bitmap)
    {
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            using var font = SystemFonts.CaptionFont ?? SystemFonts.MessageBoxFont ?? Control.DefaultFont;
            using var background = new SolidBrush(Color.FromArgb(160, Color.Black));
            using var foreground = new SolidBrush(Color.White);

            var text = string.Concat(MonitorId, " ", bitmap.Width, "x", bitmap.Height);
            if (_refreshRate > 0)
            {
                text = string.Concat(text, " @", _refreshRate, "Hz");
            }

            if (_rotation != 0)
            {
                text = string.Concat(text, " rot:", _rotation);
            }
            else if (_orientation != MonitorOrientation.Unknown)
            {
                text = string.Concat(text, " ", _orientation);
            }

            var safeFont = font ?? Control.DefaultFont;
            var safeText = text ?? string.Empty;
            var measured = graphics.MeasureString(safeText, safeFont);
            var rect = new RectangleF(4, 4, measured.Width + 8, measured.Height + 4);
            graphics.FillRectangle(background, rect);
            graphics.DrawString(safeText, safeFont, foreground, new PointF(rect.Left + 4, rect.Top + 2));
        }
        catch
        {
            // Debug overlay is best-effort only.
        }
    }
#endif

    private readonly struct BusyScope : IDisposable
    {
        private readonly MonitorPreviewHost? _owner;

        public BusyScope(MonitorPreviewHost owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            _owner?.ExitBusy();
        }
    }
}
