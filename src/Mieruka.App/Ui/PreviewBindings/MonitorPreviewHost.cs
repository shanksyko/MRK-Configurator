using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;
using Mieruka.Core.Config;
using Mieruka.Core.Diagnostics;
using Mieruka.Core.Models;
using Mieruka.Core.Monitors;
using Mieruka.Preview;
using Serilog;

namespace Mieruka.App.Ui.PreviewBindings;

/// <summary>
/// Hosts a monitor preview session and binds frames to a <see cref="WinForms.PictureBox"/>.
/// </summary>
public sealed partial class MonitorPreviewHost : IDisposable
{
    private static readonly TimeSpan DefaultFrameThrottle = PreviewSettings.CalculateFrameInterval(PreviewSettings.Default.TargetFpsGdi);
    private static readonly TimeSpan GdiFrameThrottle = PreviewSettings.Default.GetGdiFrameInterval();
    private static readonly TimeSpan GpuFrameThrottle = PreviewSettings.Default.GetGpuFrameInterval();
    private static readonly TimeSpan SafeModeFrameThrottle = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan SafeModeStartDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan LifecycleWaitDelay = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan GpuFallbackLogInterval = TimeSpan.FromSeconds(10);
    private static long _lastGpuFallbackLogTicks;

    private sealed class ReentrancyGate
    {
        private int _depth;

        public bool TryEnter(int limit = 1)
        {
            var depth = Interlocked.Increment(ref _depth);
            if (depth <= limit)
            {
                return true;
            }

            Interlocked.Decrement(ref _depth);
            return false;
        }

        public void Exit()
        {
            Interlocked.Decrement(ref _depth);
        }
    }

    private enum PreviewState
    {
        Stopped,
        Starting,
        Running,
        Pausing,
        Paused,
        Disposing,
    }

    private const int LifecycleStopped = 0;
    private const int LifecycleStarting = 1;
    private const int LifecycleRunning = 2;
    private const int LifecycleStopping = 3;

    private readonly WinForms.PictureBox _target;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private readonly object _frameTimingGate = new();
    private readonly object _stateGate = new();
    private readonly object _pendingFramesGate = new();
    private readonly List<Drawing.Bitmap> _pendingFrames = new();
    private const int PendingFrameLimit = 2;
    private Drawing.Bitmap? _previewPlaceholderBitmap;
    private bool _editorPreviewDisabledMode;
    private readonly EventHandler _frameAnimationHandler;
    private TimeSpan _frameThrottle = DefaultFrameThrottle;
    private bool _frameThrottleCustomized;
    private int _isVisible = 1;
    private Drawing.Bitmap? _currentFrame;
    private Drawing.Bitmap? _editorSnapshotBitmap;
    private bool _editorSnapshotModeEnabled;
    private int _stateRaw = (int)PreviewState.Stopped;
    private bool _disposed;
    private Drawing.Rectangle _monitorBounds;
    private Drawing.Rectangle _monitorWorkArea;
    private MonitorOrientation _orientation;
    private int _rotation;
    private int _refreshRate;
    private DateTime _nextFrameAt;
    private bool _hasActiveSession;
    private bool _isGpuActive;
    private volatile bool _isSuspended;
    private int _resumeTicket;
    private int _paused;
    private int _busy;
    private int _frameCallbackGate;
    private int _inStartStop;
    private bool _suppressEvents;
    private int _disposeRetryScheduled;
    private bool _safeModeEnabled;
    private long _safeModeResumeTicks;
    private int _safeModeDelayScheduled;
    private readonly ReentrancyGate _lifecycleGate = new();
    private long _reentrancyBlockedCount;
    private DateTime _lastGateReportUtc = DateTime.UtcNow;
    private int _lifecycleState = LifecycleStopped;

    private PreviewState State
    {
        get => (PreviewState)Volatile.Read(ref _stateRaw);
        set => Volatile.Write(ref _stateRaw, (int)value);
    }

    private bool TryTransition(PreviewState from, PreviewState to)
    {
        return Interlocked.CompareExchange(ref _stateRaw, (int)to, (int)from) == (int)from;
    }

    private bool TryTransition(ReadOnlySpan<PreviewState> fromStates, PreviewState to, out PreviewState previous)
    {
        foreach (var candidate in fromStates)
        {
            if (TryTransition(candidate, to))
            {
                previous = candidate;
                return true;
            }
        }

        previous = ReadState();
        return false;
    }

    private static Task<IMonitorCapture> CreateCaptureAsync(
        Func<IMonitorCapture> factory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return Task.Run(factory, cancellationToken);
    }

    private void SetState(PreviewState state)
    {
        State = state;
    }

    private PreviewState ReadState()
    {
        return State;
    }

    public MonitorPreviewHost(string monitorId, WinForms.PictureBox target, ILogger? logger = null)
    {
        MonitorId = monitorId ?? throw new ArgumentNullException(nameof(monitorId));
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _logger = (logger ?? Log.ForContext<MonitorPreviewHost>()).ForMonitor(MonitorId);
        _frameAnimationHandler = (_, _) =>
        {
            try
            {
                if (!_target.IsDisposed)
                {
                    _target.Invalidate();
                }
            }
            catch
            {
                // Ignore animation callbacks during disposal.
            }
        };
        EnsurePictureBoxSizeMode();
    }

    public MonitorPreviewHost(MonitorDescriptor descriptor, WinForms.PictureBox target, ILogger? logger = null)
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
    /// Gets or sets a value indicating whether the preview is visible and should process frames.
    /// </summary>
    public bool IsVisible
    {
        get => Volatile.Read(ref _isVisible) == 1;
        set => Volatile.Write(ref _isVisible, value ? 1 : 0);
    }

    public void SetEditorPreviewDisabledMode(bool enabled)
    {
        var previous = _editorPreviewDisabledMode;
        _editorPreviewDisabledMode = enabled;

        if (enabled == previous)
        {
            return;
        }

        if (enabled)
        {
            DisableEditorSnapshot(clearImage: true);
            _logger.Information(
                "EditorPreviewDisabled: usando imagem placeholder para monitor={MonitorId} // MIERUKA_FIX",
                MonitorId);
            ApplyPlaceholderFrame();
        }
        else
        {
            _logger.Information(
                "EditorPreviewDisabled: saindo do modo placeholder, preview real pode ser retomado // MIERUKA_FIX");
        }
    }

    private bool IsEditorSnapshotActive
        => _editorSnapshotModeEnabled && _editorSnapshotBitmap is not null && Volatile.Read(ref _paused) == 1;

    private bool IsEditorPreviewDisabled => _editorPreviewDisabledMode;

    /// <summary>
    /// Gets the bounds of the monitor being captured when available.
    /// </summary>
    public Drawing.Rectangle MonitorBounds => _monitorBounds;

    /// <summary>
    /// Gets the work area of the monitor being captured when available.
    /// </summary>
    public Drawing.Rectangle MonitorWorkArea => _monitorWorkArea;

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
                if (_safeModeEnabled && sanitized < SafeModeFrameThrottle)
                {
                    sanitized = SafeModeFrameThrottle;
                }

                if (!_frameThrottleCustomized && sanitized != _frameThrottle)
                {
                    _frameThrottleCustomized = true;
                }

                _frameThrottle = sanitized;
                if (_frameThrottle <= TimeSpan.Zero)
                {
                    _nextFrameAt = DateTime.MinValue;
                }
            }
        }
    }

    public bool PreviewSafeModeEnabled
    {
        get => Volatile.Read(ref _safeModeEnabled);
        set
        {
            var previous = Volatile.Read(ref _safeModeEnabled);
            if (previous == value)
            {
                return;
            }

            Volatile.Write(ref _safeModeEnabled, value);

            if (value)
            {
                lock (_frameTimingGate)
                {
                    if (_frameThrottle < SafeModeFrameThrottle)
                    {
                        _frameThrottle = SafeModeFrameThrottle;
                        _nextFrameAt = DateTime.MinValue;
                    }
                }
            }
        }
    }

    public async Task<bool> StartAsync(bool preferGpu, CancellationToken cancellationToken = default)
    {
        using var guard = new StackGuard(nameof(StartAsync));
        if (!guard.Entered)
        {
            return false;
        }

        if (!_lifecycleGate.TryEnter())
        {
            LogReentrancyBlocked(nameof(StartAsync));
            return Capture is not null;
        }

        try
        {
            var preferGpuAdjusted = preferGpu && !PreviewSafeModeEnabled;

            if (!TryEnterBusy(out var scope))
            {
                LogReentrancyBlocked(nameof(StartAsync));
                return Capture is not null;
            }

            try
            {
                return await StartCoreAsync(preferGpuAdjusted, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                scope.Dispose();
            }
        }
        finally
        {
            _lifecycleGate.Exit();
        }
    }

    /// <summary>
    /// Stops the current preview session.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        using var guard = new StackGuard(nameof(StopAsync));
        if (!guard.Entered)
        {
            return;
        }

        if (!_lifecycleGate.TryEnter())
        {
            LogReentrancyBlocked(nameof(StopAsync));
            return;
        }

        try
        {
            if (!TryEnterBusy(out var scope))
            {
                LogReentrancyBlocked(nameof(StopAsync));
                return;
            }

            try
            {
                lock (_stateGate)
                {
                    _hasActiveSession = false;
                    _isGpuActive = false;
                    _isSuspended = false;
                }

                Interlocked.Exchange(ref _paused, 0);
                await StopCoreAsync(clearFrame: true, resetPaused: true, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                scope.Dispose();
            }
        }
        finally
        {
            _lifecycleGate.Exit();
        }
    }

    /// <summary>
    /// Stops the current preview session while preventing overlapping lifecycle transitions.
    /// </summary>
    public async Task StopSafeAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var state = Volatile.Read(ref _lifecycleState);

            if (state == LifecycleStopped)
            {
                return;
            }

            if (state is LifecycleStarting or LifecycleStopping)
            {
                await WaitForLifecycleAvailabilityAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (Interlocked.CompareExchange(ref _lifecycleState, LifecycleStopping, state) != state)
            {
                await WaitForLifecycleAvailabilityAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                await StopAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Volatile.Write(ref _lifecycleState, LifecycleStopped);
            }

            return;
        }
    }

    public async Task SuspendCaptureAsync(CancellationToken cancellationToken = default)
    {
        using var guard = new StackGuard(nameof(SuspendCaptureAsync));
        if (!guard.Entered)
        {
            return;
        }

        if (_disposed)
        {
            return;
        }

        if (!TryEnterBusy(out var scope))
        {
            LogReentrancyBlocked(nameof(SuspendCaptureAsync));
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

            await StopCoreAsync(clearFrame: false, resetPaused: true, cancellationToken).ConfigureAwait(false);
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
        using var guard = new StackGuard(nameof(ResumeCapture));
        if (!guard.Entered)
        {
            return;
        }

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
        bool useGpu;

        try
        {
            ticket = Interlocked.Increment(ref _resumeTicket);

            lock (_stateGate)
            {
                shouldResume = _hasActiveSession && _isSuspended;
                useGpu = _isGpuActive;
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

        async void ResumeCore()
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

                await StartCoreAsync(useGpu, cancellationToken: CancellationToken.None).ConfigureAwait(true);
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

    public async Task PauseAsync(CancellationToken cancellationToken = default)
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
            if (IsEditorPreviewDisabled)
            {
                var wasPaused = Interlocked.Exchange(ref _paused, 1) == 1;
                await StopCoreAsync(clearFrame: true, resetPaused: false, cancellationToken).ConfigureAwait(true);
                ApplyPlaceholderFrame();

                if (!wasPaused)
                {
                    _logger.Information("PreviewPaused");
                }

                return;
            }

            var snapshotCaptured = false;
            if (IsEditorSnapshotActive)
            {
                snapshotCaptured = true;
            }
            else
            {
                snapshotCaptured = await CaptureEditorSnapshotAsync(cancellationToken).ConfigureAwait(false);
            }

            if (!snapshotCaptured)
            {
                _logger.Warning(
                    "EditorSnapshot: não foi possível capturar snapshot, mantendo preview contínuo. Motivo={Reason} // MIERUKA_FIX",
                    "SnapshotFalhou");
                return;
            }

            var wasPausedAfterSnapshot = Interlocked.Exchange(ref _paused, 1) == 1;
            var detached = await DisposeCaptureRetainingFrameAsync(resetPaused: false, cancellationToken)
                .ConfigureAwait(false);

            if (!wasPausedAfterSnapshot || detached)
            {
                _logger.Information("PreviewPaused");
            }

            if (snapshotCaptured && IsEditorSnapshotActive)
            {
                var backend = _isGpuActive ? "GPU" : "GDI";
                _logger.Information(
                    "EditorSnapshot: ativando modo snapshot para monitor={MonitorId}, backend={Backend} // MIERUKA_FIX",
                    MonitorId,
                    backend);
                _logger.Information("EditorSnapshot: modo snapshot ativado, preview contínuo pausado // MIERUKA_FIX");
            }
        }
        finally
        {
            scope.Dispose();
        }
    }

    public async Task ResumeAsync()
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
            var wasEditorPreviewDisabled = IsEditorPreviewDisabled;
            if (Volatile.Read(ref _paused) == 0)
            {
                SetEditorPreviewDisabledMode(false);
                return;
            }

            if (wasEditorPreviewDisabled)
            {
                SetEditorPreviewDisabledMode(false);
            }

            var snapshotWasActive = DisableEditorSnapshot(clearImage: true);

            bool useGpu;
            lock (_stateGate)
            {
                useGpu = _isGpuActive;
            }

            var resumed = await StartCoreAsync(useGpu, CancellationToken.None).ConfigureAwait(false);
            if (resumed)
            {
                if (snapshotWasActive)
                {
                    _logger.Information(
                        "EditorSnapshot: modo snapshot desativado, preview contínuo retomado // MIERUKA_FIX");
                }

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
        if (!_lifecycleGate.TryEnter())
        {
            ScheduleDeferredDispose();
            return;
        }

        try
        {
            _suppressEvents = true;

            if (!DisposeInternal())
            {
                ScheduleDeferredDispose();
            }
        }
        finally
        {
            _lifecycleGate.Exit();
        }
    }

    private bool DisposeInternal(bool allowRetry = false)
    {
        if (_disposed)
        {
            return true;
        }

        PreviewState previousState;
        if (!TryTransition(new[]
            {
                PreviewState.Running,
                PreviewState.Starting,
                PreviewState.Pausing,
                PreviewState.Paused,
                PreviewState.Stopped,
            }, PreviewState.Disposing, out previousState))
        {
            if (ReadState() != PreviewState.Disposing)
            {
                return false;
            }

            previousState = PreviewState.Disposing;
        }

        if (!TryEnterStartStop(nameof(Dispose), retryAction: null, out var scope))
        {
            if (previousState != PreviewState.Disposing)
            {
                SetState(previousState);
            }

            return false;
        }

        using (scope)
        {
            _disposed = true;

            lock (_stateGate)
            {
                _hasActiveSession = false;
                _isGpuActive = false;
                _isSuspended = false;
            }

            DisableEditorSnapshot(clearImage: true);

            StopCoreUnsafeAsync(clearFrame: true, resetPaused: true, cancellationToken: CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            DisposePlaceholderBitmap();

            GC.SuppressFinalize(this);
        }

        return true;
    }

    private void ScheduleDeferredDispose()
    {
        if (Interlocked.Exchange(ref _disposeRetryScheduled, 1) != 0)
        {
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                while (!DisposeInternal())
                {
                    await Task.Delay(15).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                try
                {
                    ForEvent("DisposeRetryFailed").Error(ex, "Falha ao concluir Dispose após reagendamento.");
                }
                catch
                {
                    // Swallow logging failures during shutdown.
                }
            }
            finally
            {
                Interlocked.Exchange(ref _disposeRetryScheduled, 0);
            }
        });
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

    private bool TryEnterStartStop(string operation, Action? retryAction, out StartStopScope scope)
    {
        if (Interlocked.CompareExchange(ref _inStartStop, 1, 0) != 0)
        {
            scope = default;
            if (!_suppressEvents)
            {
                LogReentrancyBlocked(operation);
                retryAction?.Invoke();
            }

            return false;
        }

        scope = new StartStopScope(this);
        return true;
    }

    private void PostDispose()
    {
        if (_suppressEvents || _target.IsDisposed)
        {
            return;
        }

        try
        {
            _target.BeginInvoke(new Action(() =>
            {
                try
                {
                    DisposeInternal(allowRetry: false);
                }
                catch (ObjectDisposedException)
                {
                }
                catch (InvalidOperationException)
                {
                }
            }));
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void ExitStartStop()
    {
        Volatile.Write(ref _inStartStop, 0);
    }

    private IEnumerable<(string Mode, Func<IMonitorCapture> Factory)> EnumerateFactories(bool preferGpu)
    {
        var monitorKey = MonitorIdentifier.Normalize(MonitorId);
        var gpuInBackoff = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763) && GraphicsCaptureProvider.IsGpuInBackoff(monitorKey);
        var hostSupportsGpu = false;
        string? monitorFriendlyName = null;

        if (!gpuInBackoff)
        {
            MonitorInfo? monitor = null;
            try
            {
                monitor = MonitorLocator.Find(monitorKey);
            }
            catch
            {
                monitor = null;
            }

            if (monitor is not null)
            {
                monitorFriendlyName = GetMonitorFriendlyName(monitor);
                hostSupportsGpu = CaptureFactory.IsHostSuitableForWgc(monitor);
                if (!hostSupportsGpu)
                {
                    gpuInBackoff = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763) && GraphicsCaptureProvider.IsGpuInBackoff(monitorKey);
                }
            }
        }

        if (gpuInBackoff)
        {
            monitorFriendlyName ??= MonitorId;
            LogGpuFallback(monitorKey, monitorFriendlyName);
        }

        if (PreviewSafeModeEnabled)
        {
            yield return ("GDI", () => CaptureFactory.Gdi(MonitorId));
            yield break;
        }

        if (preferGpu && !gpuInBackoff && hostSupportsGpu)
        {
            yield return ("GPU", () => CaptureFactory.Gpu(MonitorId));
            yield return ("GDI", () => CaptureFactory.Gdi(MonitorId));
            yield break;
        }

        yield return ("GDI", () => CaptureFactory.Gdi(MonitorId));

        if (!gpuInBackoff && hostSupportsGpu)
        {
            yield return ("GPU", () => CaptureFactory.Gpu(MonitorId));
        }
    }

    private async Task<bool> StartCoreAsync(bool preferGpu, CancellationToken cancellationToken)
    {
        using var guard = new StackGuard(nameof(StartCoreAsync));
        if (!guard.Entered)
        {
            return false;
        }

        if (_disposed)
        {
            return false;
        }

        if (PreviewSafeModeEnabled)
        {
            preferGpu = false;

            if (ShouldDelayStartForSafeMode(out var remainingDelay))
            {
                ScheduleSafeModeStartRetry(remainingDelay);
                return false;
            }

            Interlocked.Exchange(ref _safeModeResumeTicks, 0);
        }
        else
        {
            Interlocked.Exchange(ref _safeModeResumeTicks, 0);
        }

        if (ReadState() == PreviewState.Disposing)
        {
            return false;
        }

        if (!HasUsableTargetArea())
        {
            Interlocked.Exchange(ref _paused, 1);

            lock (_stateGate)
            {
                _hasActiveSession = false;
                _isGpuActive = false;
                _isSuspended = false;
            }

            await StopCoreAsync(clearFrame: false, resetPaused: false, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _lifecycleState, LifecycleStopped);
            return false;
        }

        PreviewState previousState = PreviewState.Stopped;
        var transitionRequested = false;

        while (true)
        {
            var current = ReadState();
            if (current is PreviewState.Running or PreviewState.Starting)
            {
                Volatile.Write(ref _lifecycleState, LifecycleRunning);
                return true;
            }

            if (current == PreviewState.Disposing)
            {
                return false;
            }

            if (current == PreviewState.Pausing)
            {
                PostStart(preferGpu);
                return false;
            }

            if (current is PreviewState.Stopped or PreviewState.Paused)
            {
                if (TryTransition(current, PreviewState.Starting))
                {
                    previousState = current;
                    transitionRequested = true;
                    break;
                }

                continue;
            }

            break;
        }

        if (!transitionRequested)
        {
            return false;
        }

        if (!TryEnterStartStop(nameof(StartAsync), () => PostStart(preferGpu), out var scope))
        {
            SetState(previousState);

            if (_suppressEvents)
            {
                lock (_gate)
                {
                    var running = Capture is not null;
                    Volatile.Write(ref _lifecycleState, running ? LifecycleRunning : LifecycleStopped);
                    return running;
                }
            }

            Volatile.Write(ref _lifecycleState, LifecycleRunning);
            return true;
        }

        using (scope)
        {
            var started = await StartCoreUnsafeAsync(preferGpu, cancellationToken).ConfigureAwait(false);
            if (started)
            {
                SetState(PreviewState.Running);
                Volatile.Write(ref _lifecycleState, LifecycleRunning);
            }
            else
            {
                SetState(previousState);
                Volatile.Write(ref _lifecycleState, LifecycleStopped);
            }

            return started;
        }
    }

    public async Task<bool> StartSafeAsync(bool preferGpu, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var state = Volatile.Read(ref _lifecycleState);

            if (state == LifecycleRunning)
            {
                return true;
            }

            if (state is LifecycleStarting or LifecycleStopping)
            {
                await WaitForLifecycleAvailabilityAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (Interlocked.CompareExchange(ref _lifecycleState, LifecycleStarting, state) != state)
            {
                await WaitForLifecycleAvailabilityAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            var started = false;

            try
            {
                started = await StartAsync(preferGpu, cancellationToken).ConfigureAwait(false);
                return started;
            }
            finally
            {
                Volatile.Write(ref _lifecycleState, started ? LifecycleRunning : LifecycleStopped);
            }
        }
    }

    private static Task WaitForLifecycleAvailabilityAsync(CancellationToken cancellationToken)
    {
        return Task.Delay(LifecycleWaitDelay, cancellationToken);
    }

    private bool ShouldDelayStartForSafeMode(out TimeSpan remainingDelay)
    {
        remainingDelay = TimeSpan.Zero;

        if (!PreviewSafeModeEnabled)
        {
            Interlocked.Exchange(ref _safeModeResumeTicks, 0);
            return false;
        }

        var now = DateTime.UtcNow;
        var resumeTicks = Volatile.Read(ref _safeModeResumeTicks);

        if (resumeTicks == 0)
        {
            var resumeAt = now.Add(SafeModeStartDelay).Ticks;
            var previous = Interlocked.CompareExchange(ref _safeModeResumeTicks, resumeAt, 0);
            resumeTicks = previous == 0 ? resumeAt : previous;
        }

        var resumeAtUtc = new DateTime(resumeTicks, DateTimeKind.Utc);

        if (now < resumeAtUtc)
        {
            remainingDelay = resumeAtUtc - now;
            return true;
        }

        Interlocked.Exchange(ref _safeModeResumeTicks, 0);
        return false;
    }

    private void ScheduleSafeModeStartRetry(TimeSpan delay)
    {
        if (delay < TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
        }

        if (Interlocked.CompareExchange(ref _safeModeDelayScheduled, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay).ConfigureAwait(false);
                }
                else
                {
                    await Task.Yield();
                }
            }
            catch (TaskCanceledException)
            {
            }
            finally
            {
                Interlocked.Exchange(ref _safeModeDelayScheduled, 0);
            }

            if (_disposed || _suppressEvents)
            {
                return;
            }

            if (_target.IsDisposed)
            {
                return;
            }

            try
            {
                _target.BeginInvoke(new Action(async () =>
                {
                    if (_suppressEvents || _disposed || _target.IsDisposed)
                    {
                        return;
                    }

                    var state = ReadState();
                    if (state is PreviewState.Disposing or PreviewState.Running or PreviewState.Starting)
                    {
                        return;
                    }

                    try
                    {
                        await StartSafeAsync(preferGpu: false).ConfigureAwait(true);
                    }
                    catch (Exception ex)
                    {
                        ForEvent("SafeModeStartRetryFailed")
                            .Error(ex, "Falha ao reiniciar a pré-visualização em modo seguro.");
                        throw;
                    }
                }));
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        });
    }

    private async Task<bool> StartCoreUnsafeAsync(bool preferGpu, CancellationToken cancellationToken)
    {
        using var guard = new StackGuard(nameof(StartCoreUnsafeAsync));
        if (!guard.Entered)
        {
            return false;
        }

        if (_disposed)
        {
            return false;
        }

        if (PreviewSafeModeEnabled)
        {
            preferGpu = false;
            try
            {
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
            catch (ThreadInterruptedException)
            {
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        lock (_gate)
        {
            if (Capture is not null)
            {
                return true;
            }
        }

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        if (UseExternalPreviewHost && !PreviewSafeModeEnabled)
        {
            var externalStarted = await TryStartExternalAsync(preferGpu, cancellationToken).ConfigureAwait(false);
            if (externalStarted)
            {
                Interlocked.Exchange(ref _paused, 0);
                return true;
            }
        }

        if (!PopulateMetadataFromMonitor())
        {
            return false;
        }

        foreach (var (mode, factory) in EnumerateFactories(preferGpu))
        {
            var isGpu = string.Equals(mode, "GPU", StringComparison.OrdinalIgnoreCase);
            IMonitorCapture? capture = null;
            try
            {
                capture = await CreateCaptureAsync(factory, cancellationToken).ConfigureAwait(false);
                capture.FrameArrived += OnFrameArrived;

                UpdateFrameThrottleForCapture(capture);

                lock (_gate)
                {
                    Capture = capture;
                    Interlocked.Exchange(ref _paused, 0);
                }

                lock (_stateGate)
                {
                    _hasActiveSession = true;
                    _isGpuActive = isGpu;
                    _isSuspended = false;
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                if (capture is not null)
                {
                    capture.FrameArrived -= OnFrameArrived;
                    await SafeDisposeAsync(capture, cancellationToken).ConfigureAwait(false);
                }

                return false;
            }
            catch (Exception ex)
            {
                var reason = $"{ex.GetType().Name}: {ex.Message}";
                if (preferGpu && isGpu)
                {
                    ForEvent("MonitorFallback")
                        .Warning(
                            ex,
                            "Fallback para GDI habilitado. Monitor={MonitorId}; Fábrica={Factory}; Motivo={Reason}",
                            MonitorId,
                            mode,
                            reason);
                }
                else
                {
                    if (!_suppressEvents)
                    {
                        _logger.Warning(
                            ex,
                            "Falha ao iniciar captura ({Mode}) para {MonitorId}. Motivo={Reason}",
                            mode,
                            MonitorId,
                            reason);
                    }
                }

                if (isGpu)
                {
                    EvaluateGpuDisable(ex);
                }

                if (capture is not null)
                {
                    capture.FrameArrived -= OnFrameArrived;
                    await SafeDisposeAsync(capture, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        lock (_stateGate)
        {
            if (!_isSuspended)
            {
                _hasActiveSession = false;
                _isGpuActive = false;
            }
        }

        return false;
    }

    private static void EvaluateGpuDisable(Exception exception)
    {
        switch (exception)
        {
            case GraphicsCaptureUnavailableException unavailable when unavailable.IsPermanent:
                GpuCaptureGuard.DisableGpuPermanently("GraphicsCaptureUnavailableException");
                break;
            case ArgumentException:
            case NotSupportedException:
            case InvalidOperationException:
            case System.ComponentModel.Win32Exception:
            case System.Runtime.InteropServices.COMException:
                GpuCaptureGuard.DisableGpuPermanently($"{exception.GetType().Name}: {exception.Message}");
                break;
        }
    }

    private void OnFrameArrived(object? sender, MonitorFrameArrivedEventArgs e)
    {
        using var guard = new StackGuard(nameof(OnFrameArrived));
        if (!guard.Entered)
        {
            e.Dispose();
            return;
        }

        if (_suppressEvents)
        {
            e.Dispose();
            return;
        }

        if (ReadState() == PreviewState.Disposing)
        {
            e.Dispose();
            return;
        }

        if (Interlocked.Exchange(ref _frameCallbackGate, 1) != 0)
        {
            Interlocked.Increment(ref _reentrancyBlockedCount);
            MaybeReportGateBlocks();
            e.Dispose();
            return;
        }

        try
        {
            if (Volatile.Read(ref _paused) == 1)
            {
                _logger.Debug("FrameDroppedPaused");
                e.Dispose();
                return;
            }

            if (IsEditorPreviewDisabled)
            {
                _logger.Debug(
                    "[DBG] EditorPreviewDisabled: descartando frame de preview ao vivo monitor={MonitorId} // MIERUKA_FIX",
                    MonitorId);
                e.Dispose();
                return;
            }

            if (IsEditorSnapshotActive)
            {
                _logger.Debug("EditorSnapshot: quadro ignorado porque snapshot está ativo // MIERUKA_FIX");
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

            Drawing.Bitmap? clone = null;
            try
            {
                var bitmap = e.Frame;
                if (!TryGetFrameSize(bitmap, out var width, out var height))
                {
                    return;
                }

                if (width <= 0 || height <= 0)
                {
                    ForEvent("FrameDiscarded").Debug(
                        "Quadro de pré-visualização descartado por dimensões inválidas {Width}x{Height}.",
                        width,
                        height);
                    return;
                }

                clone = TryCloneFrame(bitmap, width, height);
#if DEBUG
                if (clone is not null)
                {
                    DrawDebugOverlay(clone);
                }
#endif
            }
            finally
            {
                e.Dispose();
            }

            if (clone is null)
            {
                return;
            }

            if (!RegisterPendingFrame(clone, out var pendingCount))
            {
                ForEvent("FrameDroppedBackpressure").Debug(
                    "Quadro de pré-visualização descartado por backpressure. Pendentes={PendingCount} Limite={Limit}.",
                    pendingCount,
                    PendingFrameLimit);
                clone.Dispose();
                return;
            }

            UpdateTarget(clone);
        }
        finally
        {
            Interlocked.Exchange(ref _frameCallbackGate, 0);
        }
    }

    private void MaybeReportGateBlocks()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastGateReportUtc).TotalSeconds < 5)
        {
            return;
        }

        var blocked = Interlocked.Exchange(ref _reentrancyBlockedCount, 0);
        if (blocked > 0)
        {
            _logger.Warning("Reentrancy blocked events={Count}", blocked);
        }

        _lastGateReportUtc = now;
    }

    private bool IsPlaceholderImage(Drawing.Image? image)
        => image is not null && ReferenceEquals(image, _previewPlaceholderBitmap);

    private Drawing.Size GetPlaceholderTargetSize()
    {
        try
        {
            var size = _target.ClientSize;
            if (size.Width > 0 && size.Height > 0)
            {
                return size;
            }
        }
        catch
        {
            // Ignore size retrieval failures; fall back to a default placeholder size.
        }

        return new Drawing.Size(800, 450);
    }

    private void EnsurePlaceholderBitmap(Drawing.Size targetSize)
    {
        var width = Math.Max(1, targetSize.Width);
        var height = Math.Max(1, targetSize.Height);

        if (_previewPlaceholderBitmap is { } existing
            && existing.Width == width
            && existing.Height == height)
        {
            return;
        }

        var bitmap = new Drawing.Bitmap(width, height);
        using (var g = Drawing.Graphics.FromImage(bitmap))
        {
            g.Clear(Drawing.Color.FromArgb(30, 30, 30));

            using var pen = new Drawing.Pen(Drawing.Color.FromArgb(60, 60, 60));
            var step = Math.Max(20, Math.Min(width, height) / 10);
            for (var x = 0; x < width; x += step)
            {
                g.DrawLine(pen, x, 0, x, height);
            }

            for (var y = 0; y < height; y += step)
            {
                g.DrawLine(pen, 0, y, width, y);
            }

            using var brush = new Drawing.SolidBrush(Drawing.Color.FromArgb(120, 200, 200));
            using var font = new Drawing.Font(
                "Segoe UI",
                (float)Math.Max(10, Math.Min(width, height) / 24f),
                Drawing.FontStyle.Bold,
                Drawing.GraphicsUnit.Pixel);
            var text = "PREVIEW DESATIVADO";
            var textSize = g.MeasureString(text, font);
            var origin = new Drawing.PointF(
                (width - textSize.Width) / 2f,
                (height - textSize.Height) / 2f);
            g.DrawString(text, font, brush, origin);
        }

        var previous = Interlocked.Exchange(ref _previewPlaceholderBitmap, bitmap);
        if (previous is not null && !ReferenceEquals(previous, bitmap))
        {
            try
            {
                previous.Dispose();
            }
            catch
            {
            }
        }
    }

    private Drawing.Bitmap? TryCloneFrame(Drawing.Image frame, int width, int height)
    {
        try
        {
            return new Drawing.Bitmap(frame);
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException or InvalidOperationException)
        {
            ForEvent("FrameDiscarded").Debug(
                ex,
                "Quadro de pré-visualização descartado por falha ao clonar frame {Width}x{Height}.",
                width,
                height);
            return null;
        }
        catch (Exception ex) when (ex is OutOfMemoryException)
        {
            ForEvent("FrameDiscarded").Warning(
                ex,
                "Quadro de pré-visualização descartado por falta de memória ao clonar frame {Width}x{Height}.",
                width,
                height);
            return null;
        }
    }

    private bool TryApplyFrame(Drawing.Bitmap frame)
    {
        if (!TryGetFrameSize(frame, out var width, out var height))
        {
            frame.Dispose();
            return false;
        }

        if (width <= 0 || height <= 0)
        {
            ForEvent("FrameDiscarded").Debug(
                "Quadro de pré-visualização descartado por dimensões inválidas {Width}x{Height}.",
                width,
                height);
            frame.Dispose();
            return false;
        }

        var previous = Interlocked.Exchange(ref _currentFrame, frame);
        var animationWasStopped = false;

        if (previous is not null)
        {
            animationWasStopped = StopAnimationSafe(previous);
        }

        try
        {
            _target.Image = frame;
            StartAnimationSafe(frame);
        }
        catch (Exception ex) when (ex is ArgumentException or ObjectDisposedException or ExternalException)
        {
            ForEvent("FrameDiscarded").Debug(ex, "Quadro de pré-visualização descartado por imagem inválida.");
            StopAnimationSafe(frame);
            frame.Dispose();

            if (previous is not null)
            {
                if (animationWasStopped)
                {
                    StartAnimationSafe(previous);
                }

                Interlocked.Exchange(ref _currentFrame, previous);
                _target.Image = previous;
            }
            else
            {
                Interlocked.Exchange(ref _currentFrame, null);
                _target.Image = null;
            }

            return false;
        }
        finally
        {
            if (previous is not null && !ReferenceEquals(previous, _currentFrame) && !IsPlaceholderImage(previous))
            {
                DisposeFrame(previous);
            }
        }

        return true;
    }

    private void ApplyPlaceholderFrame()
    {
        using var guard = new StackGuard(nameof(ApplyPlaceholderFrame));
        if (!guard.Entered)
        {
            return;
        }

        if (_suppressEvents || _target.IsDisposed)
        {
            return;
        }

        if (_target.InvokeRequired)
        {
            try
            {
                _target.BeginInvoke(new Action(ApplyPlaceholderFrame));
            }
            catch
            {
                // Ignore invoke failures during shutdown.
            }

            return;
        }

        var previous = Interlocked.Exchange(ref _currentFrame, null);
        if (previous is not null)
        {
            StopAnimationSafe(previous);
        }

        var targetSize = GetPlaceholderTargetSize();
        EnsurePlaceholderBitmap(targetSize);
        var placeholder = _previewPlaceholderBitmap;
        if (placeholder is null)
        {
            if (previous is not null && !IsPlaceholderImage(previous))
            {
                DisposeFrame(previous);
            }

            return;
        }

        _target.Image = placeholder;

        if (previous is not null && !ReferenceEquals(previous, placeholder) && !IsPlaceholderImage(previous))
        {
            DisposeFrame(previous);
        }
    }

    private void UpdateTarget(Drawing.Bitmap frame)
    {
        using var guard = new StackGuard(nameof(UpdateTarget));
        if (!guard.Entered)
        {
            UnregisterPendingFrame(frame);
            frame.Dispose();
            return;
        }

        if (IsEditorPreviewDisabled)
        {
            UnregisterPendingFrame(frame);
            frame.Dispose();
            ApplyPlaceholderFrame();
            return;
        }

        if (_suppressEvents)
        {
            UnregisterPendingFrame(frame);
            frame.Dispose();
            return;
        }

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
                _target.BeginInvoke(new Action<Drawing.Bitmap>(UpdateTarget), frame);
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

        TryApplyFrame(frame);
    }

    private Drawing.Image? GetLatestFrameForSnapshot()
    {
        var frame = Interlocked.CompareExchange(ref _currentFrame, null, null);
        if (frame is not null)
        {
            if (!IsPlaceholderImage(frame))
            {
                return frame;
            }
        }

        lock (_pendingFramesGate)
        {
            if (_pendingFrames.Count > 0)
            {
                return _pendingFrames[^1];
            }
        }

        try
        {
            if (_target.IsDisposed)
            {
                return null;
            }

            if (_target.InvokeRequired)
            {
                return _target.Invoke(new Func<Drawing.Image?>(() =>
                {
                    var image = _target.Image;
                    return IsPlaceholderImage(image) ? null : image;
                }));
            }

            var targetImage = _target.Image;
            return IsPlaceholderImage(targetImage) ? null : targetImage;
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> CaptureEditorSnapshotAsync(CancellationToken cancellationToken)
    {
        if (_disposed || _target.IsDisposed)
        {
            return false;
        }

        if (IsEditorPreviewDisabled)
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var source = GetLatestFrameForSnapshot();
        if (source is null)
        {
            _logger.Warning("EditorSnapshot: não foi possível capturar snapshot, mantendo preview contínuo. Motivo={Reason} // MIERUKA_FIX", "SemFrame");
            return false;
        }

        if (!TryGetFrameSize(source, out var width, out var height) || width <= 0 || height <= 0)
        {
            _logger.Warning("EditorSnapshot: não foi possível capturar snapshot, mantendo preview contínuo. Motivo={Reason} // MIERUKA_FIX", "FrameInvalido");
            return false;
        }

        Drawing.Bitmap? snapshot = null;

        try
        {
            snapshot = new Drawing.Bitmap(source);
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException or InvalidOperationException)
        {
            _logger.Warning(ex, "EditorSnapshot: não foi possível clonar frame para snapshot // MIERUKA_FIX");
            return false;
        }
        catch (Exception ex) when (ex is OutOfMemoryException)
        {
            _logger.Warning(ex, "EditorSnapshot: falha de memória ao clonar frame para snapshot // MIERUKA_FIX");
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var applied = await RenderEditorSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(true);
        if (applied)
        {
            _logger.Information(
                "EditorSnapshot: snapshot capturado para monitor {MonitorId}, bounds={Width}x{Height} // MIERUKA_FIX",
                MonitorId,
                width,
                height);
        }

        return applied;
    }

    private Task<bool> RenderEditorSnapshotAsync(Drawing.Bitmap snapshot, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Apply()
        {
            if (cancellationToken.IsCancellationRequested || _disposed || _target.IsDisposed)
            {
                TryDisposeSnapshot(snapshot);
                completion.TrySetResult(false);
                return;
            }

            var previousSnapshot = Interlocked.Exchange(ref _editorSnapshotBitmap, snapshot);
            _editorSnapshotModeEnabled = true;

            if (!TryApplyFrame(snapshot))
            {
                _editorSnapshotModeEnabled = false;
                Interlocked.Exchange(ref _editorSnapshotBitmap, null);
                if (previousSnapshot is not null && !ReferenceEquals(previousSnapshot, snapshot))
                {
                    TryDisposeSnapshot(previousSnapshot);
                }

                completion.TrySetResult(false);
                return;
            }

            if (previousSnapshot is not null && !ReferenceEquals(previousSnapshot, snapshot))
            {
                TryDisposeSnapshot(previousSnapshot);
            }

            _logger.Debug(
                "EditorSnapshot: overlays aplicados sobre snapshot estático monitor={MonitorId} // MIERUKA_FIX",
                MonitorId);
            completion.TrySetResult(true);
        }

        try
        {
            if (_target.InvokeRequired)
            {
                _target.BeginInvoke(new Action(Apply));
            }
            else
            {
                Apply();
            }
        }
        catch
        {
            TryDisposeSnapshot(snapshot);
            completion.TrySetResult(false);
        }

        return completion.Task;
    }

    private bool DisableEditorSnapshot(bool clearImage)
    {
        var snapshot = Interlocked.Exchange(ref _editorSnapshotBitmap, null);
        var hadSnapshot = snapshot is not null || _editorSnapshotModeEnabled;
        _editorSnapshotModeEnabled = false;

        if (snapshot is null)
        {
            return hadSnapshot;
        }

        void DisposeSnapshot()
        {
            if (clearImage && ReferenceEquals(_currentFrame, snapshot))
            {
                StopAnimationSafe(snapshot);
                Interlocked.Exchange(ref _currentFrame, null);

                if (!_target.IsDisposed && ReferenceEquals(_target.Image, snapshot))
                {
                    _target.Image = null;
                }
            }

            TryDisposeSnapshot(snapshot);
        }

        if (_target.InvokeRequired)
        {
            try
            {
                _target.BeginInvoke(new Action(DisposeSnapshot));
            }
            catch
            {
                TryDisposeSnapshot(snapshot);
            }
        }
        else
        {
            DisposeSnapshot();
        }

        return hadSnapshot;
    }

    private static void TryDisposeSnapshot(Drawing.Bitmap snapshot)
    {
        try
        {
            snapshot.Dispose();
        }
        catch
        {
        }
    }

    private bool TryGetFrameSize(Drawing.Image frame, out int width, out int height)
    {
        try
        {
            width = frame.Width;
            height = frame.Height;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or ObjectDisposedException or ExternalException)
        {
            ForEvent("FrameDiscarded").Debug(ex, "Quadro de pré-visualização descartado por imagem inválida.");
            width = 0;
            height = 0;
            return false;
        }
    }

    private bool StopAnimationSafe(Drawing.Image image)
    {
        if (!CanAnimate(image))
        {
            return false;
        }

        try
        {
            Drawing.ImageAnimator.StopAnimate(image, _frameAnimationHandler);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void StartAnimationSafe(Drawing.Image image)
    {
        if (!CanAnimate(image))
        {
            return;
        }

        try
        {
            Drawing.ImageAnimator.Animate(image, _frameAnimationHandler);
        }
        catch
        {
            // Ignore animation failures.
        }
    }

    private static bool CanAnimate(Drawing.Image image)
    {
        try
        {
            return Drawing.ImageAnimator.CanAnimate(image);
        }
        catch (Exception ex) when (ex is ArgumentException or ObjectDisposedException or ExternalException)
        {
            return false;
        }
    }

    private static void DisposeFrame(Drawing.Image frame)
    {
        try
        {
            frame.Dispose();
        }
        catch
        {
            // Ignore dispose failures.
        }
    }

    private void ClearFrame()
    {
        using var guard = new StackGuard(nameof(ClearFrame));
        if (!guard.Entered)
        {
            return;
        }

        if (_target.IsDisposed)
        {
            if (_currentFrame is not null)
            {
                StopAnimationSafe(_currentFrame);
                DisposeFrame(_currentFrame);
            }
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

        if (ReferenceEquals(_target.Image, _currentFrame) || IsPlaceholderImage(_target.Image))
        {
            _target.Image = null;
        }

        if (_currentFrame is not null)
        {
            StopAnimationSafe(_currentFrame);
            if (!IsPlaceholderImage(_currentFrame))
            {
                DisposeFrame(_currentFrame);
            }
            _currentFrame = null;
        }
        ResetFrameThrottle();
    }

    private void DisposePlaceholderBitmap()
    {
        var placeholder = Interlocked.Exchange(ref _previewPlaceholderBitmap, null);
        if (placeholder is null)
        {
            return;
        }

        try
        {
            placeholder.Dispose();
        }
        catch
        {
        }
    }

    private async Task StopCoreAsync(bool clearFrame, bool resetPaused, CancellationToken cancellationToken)
    {
        using var guard = new StackGuard(nameof(StopCoreAsync));
        if (!guard.Entered)
        {
            return;
        }

        if (ReadState() == PreviewState.Disposing)
        {
            return;
        }

        var targetState = clearFrame ? PreviewState.Stopped : PreviewState.Paused;
        PreviewState previousState;

        ReadOnlySpan<PreviewState> candidates = clearFrame
            ? new[] { PreviewState.Running, PreviewState.Starting, PreviewState.Paused }
            : new[] { PreviewState.Running, PreviewState.Starting };

        if (!TryTransition(candidates, PreviewState.Pausing, out previousState))
        {
            if (ReadState() == targetState)
            {
                if (clearFrame)
                {
                    ClearFrame();
                }

                return;
            }

            if (!clearFrame && ReadState() == PreviewState.Pausing)
            {
                PostStop(clearFrame, resetPaused);
            }

            return;
        }

        if (!TryEnterStartStop(nameof(StopAsync), () => PostStop(clearFrame, resetPaused), out var scope))
        {
            SetState(previousState);
            return;
        }

        using (scope)
        {
            await StopCoreUnsafeAsync(clearFrame, resetPaused, cancellationToken).ConfigureAwait(false);
            SetState(targetState);
        }
    }

    private async Task StopCoreUnsafeAsync(bool clearFrame, bool resetPaused, CancellationToken cancellationToken)
    {
        using var guard = new StackGuard(nameof(StopCoreUnsafeAsync));
        if (!guard.Entered)
        {
            return;
        }

        await StopExternalAsync().ConfigureAwait(false);

        IMonitorCapture? capture;
        lock (_gate)
        {
            capture = Capture;
            Capture = null;
        }

        if (capture is not null)
        {
            capture.FrameArrived -= OnFrameArrived;
            await SafeDisposeAsync(capture, cancellationToken).ConfigureAwait(false);
            if (!_suppressEvents)
            {
                _logger.Information("CaptureDisposed");
            }
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

        Volatile.Write(ref _lifecycleState, LifecycleStopped);
    }

    private async Task<bool> DisposeCaptureRetainingFrameAsync(bool resetPaused, CancellationToken cancellationToken)
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

        await StopCoreAsync(clearFrame: false, resetPaused: resetPaused, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private void PostStart(bool preferGpu)
    {
        using var guard = new StackGuard(nameof(PostStart));
        if (!guard.Entered)
        {
            return;
        }

        if (_suppressEvents || _disposed || _target.IsDisposed)
        {
            return;
        }

        try
        {
            _target.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await StartCoreAsync(preferGpu, CancellationToken.None).ConfigureAwait(true);
                }
                catch (ObjectDisposedException)
                {
                }
                catch (InvalidOperationException)
                {
                }
            }));
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void PostStop(bool clearFrame, bool resetPaused)
    {
        using var guard = new StackGuard(nameof(PostStop));
        if (!guard.Entered)
        {
            return;
        }

        if (_suppressEvents || _target.IsDisposed)
        {
            return;
        }

        try
        {
            _target.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await StopCoreAsync(clearFrame, resetPaused, CancellationToken.None).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
                catch (InvalidOperationException)
                {
                }
            }));
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private bool ShouldProcessFrame()
    {
        if (IsEditorSnapshotActive)
        {
            return false;
        }

        if (IsEditorPreviewDisabled)
        {
            return false;
        }

        if (!IsVisible)
        {
            return false;
        }

        if (!HasPendingFrameCapacity())
        {
            return false;
        }

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

    private bool HasPendingFrameCapacity()
    {
        int pendingCount;
        lock (_pendingFramesGate)
        {
            if (_pendingFrames.Count < PendingFrameLimit)
            {
                return true;
            }

            pendingCount = _pendingFrames.Count;
        }

        ForEvent("FrameDroppedBackpressure").Debug(
            "Limite de fila atingido; quadro mais recente será descartado. Pendentes={Pending} Limite={Limit}",
            pendingCount,
            PendingFrameLimit);
        return false;
    }

    private void ResetFrameThrottle()
    {
        lock (_frameTimingGate)
        {
            _nextFrameAt = DateTime.MinValue;
        }
    }

    private void UpdateFrameThrottleForCapture(IMonitorCapture capture)
    {
        lock (_frameTimingGate)
        {
            if (_frameThrottleCustomized)
            {
                return;
            }

            if (_safeModeEnabled)
            {
                _frameThrottle = SafeModeFrameThrottle;
                _nextFrameAt = DateTime.MinValue;
                return;
            }

            var targetThrottle = capture is GraphicsCaptureProvider ? GpuFrameThrottle : GdiFrameThrottle;
            _frameThrottle = targetThrottle;
            if (_frameThrottle <= TimeSpan.Zero)
            {
                _nextFrameAt = DateTime.MinValue;
            }
        }
    }

    private bool ShouldDisplayFrame()
    {
        if (IsEditorSnapshotActive)
        {
            return false;
        }

        if (IsEditorPreviewDisabled)
        {
            return false;
        }

        if (Volatile.Read(ref _paused) == 1 || _isSuspended || !IsVisible)
        {
            return false;
        }

        lock (_stateGate)
        {
            return _hasActiveSession;
        }
    }

    private bool RegisterPendingFrame(Drawing.Bitmap frame, out int pendingCount)
    {
        lock (_pendingFramesGate)
        {
            if (_pendingFrames.Count >= PendingFrameLimit)
            {
                pendingCount = _pendingFrames.Count;
                return false;
            }

            _pendingFrames.Add(frame);
            pendingCount = _pendingFrames.Count;
        }

        return true;
    }

    private void UnregisterPendingFrame(Drawing.Bitmap frame)
    {
        lock (_pendingFramesGate)
        {
            var previousCount = _pendingFrames.Count;
            _pendingFrames.Remove(frame);
            if (_pendingFrames.Count == 0 && previousCount > 0)
            {
                ForEvent("FrameQueueIdle").Debug("Fila de frames vazia; preview ocioso.");
            }
        }
    }

    private void DisposePendingFrames()
    {
        Drawing.Bitmap[]? frames = null;

        lock (_pendingFramesGate)
        {
            if (_pendingFrames.Count > 0)
            {
                frames = _pendingFrames.ToArray();
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

    private static async ValueTask SafeDisposeAsync(IMonitorCapture capture, CancellationToken cancellationToken)
    {
        try
        {
            var stopTask = capture.StopAsync();
            if (!stopTask.IsCompletedSuccessfully)
            {
                await stopTask.AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                stopTask.GetAwaiter().GetResult();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Ignore stop failures.
        }

        try
        {
            var disposeTask = capture.DisposeAsync().AsTask();
            if (!disposeTask.IsCompletedSuccessfully)
            {
                await disposeTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await disposeTask.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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
    {
        if (_suppressEvents)
        {
            return;
        }

        Interlocked.Increment(ref _reentrancyBlockedCount);
        MaybeReportGateBlocks();

        ForEvent("ReentrancyBlocked").Debug("Operação {Operation} bloqueada por reentrância.", operation);
    }

    private static string GetMonitorFriendlyName(MonitorInfo monitor)
    {
        if (!string.IsNullOrWhiteSpace(monitor.Name))
        {
            return monitor.Name;
        }

        if (!string.IsNullOrWhiteSpace(monitor.DeviceName))
        {
            return monitor.DeviceName;
        }

        return MonitorIdentifier.Create(monitor);
    }

    private void LogGpuFallback(string monitorKey, string monitorFriendlyName)
    {
        if (_suppressEvents)
        {
            return;
        }

        var nowTicks = DateTime.UtcNow.Ticks;
        var previous = Volatile.Read(ref _lastGpuFallbackLogTicks);
        if (previous != 0)
        {
            var elapsed = nowTicks - previous;
            if (elapsed >= 0 && elapsed < GpuFallbackLogInterval.Ticks)
            {
                return;
            }
        }

        Interlocked.Exchange(ref _lastGpuFallbackLogTicks, nowTicks);
        _logger.Information(
            "GpuCaptureFallbackAtivado monitor={MonitorFriendly} key={MonitorKey}",
            monitorFriendlyName,
            monitorKey);
    }

    private void EnsurePictureBoxSizeMode()
    {
        if (_target.SizeMode != WinForms.PictureBoxSizeMode.Zoom)
        {
            _target.SizeMode = WinForms.PictureBoxSizeMode.Zoom;
        }

        EnableDoubleBuffering(_target);
    }

    private bool HasUsableTargetArea()
    {
        if (_target.IsDisposed)
        {
            return false;
        }

        try
        {
            var width = _target.Width;
            var height = _target.Height;
            return width > 0 && height > 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            return false;
        }
    }

    private static void EnableDoubleBuffering(WinForms.PictureBox target)
    {
        try
        {
            typeof(WinForms.Control)
                .GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)?
                .SetValue(target, true);
        }
        catch
        {
            // Ignore failures when enabling double buffering; the preview will continue without it.
        }
    }

    private bool PopulateMetadataFromMonitor()
    {
        if (_monitorBounds != Drawing.Rectangle.Empty)
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
            if (info.Bounds != Drawing.Rectangle.Empty)
            {
                _monitorBounds = info.Bounds;
            }

            if (info.WorkArea != Drawing.Rectangle.Empty)
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

            if (_monitorBounds == Drawing.Rectangle.Empty && info.Width > 0 && info.Height > 0)
            {
                _monitorBounds = new Drawing.Rectangle(0, 0, info.Width, info.Height);
            }

            if (_monitorWorkArea == Drawing.Rectangle.Empty && _monitorBounds != Drawing.Rectangle.Empty)
            {
                _monitorWorkArea = _monitorBounds;
            }

            if (_monitorBounds != Drawing.Rectangle.Empty)
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

            if (fallback.Bounds != Drawing.Rectangle.Empty)
            {
                _monitorBounds = fallback.Bounds;
            }
            else if (fallback.Width > 0 && fallback.Height > 0)
            {
                _monitorBounds = new Drawing.Rectangle(0, 0, fallback.Width, fallback.Height);
            }

            if (fallback.WorkArea != Drawing.Rectangle.Empty)
            {
                _monitorWorkArea = fallback.WorkArea;
            }
            else if (_monitorBounds != Drawing.Rectangle.Empty)
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

            return _monitorBounds != Drawing.Rectangle.Empty;
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
    private void DrawDebugOverlay(Drawing.Bitmap bitmap)
    {
        try
        {
            using var graphics = Drawing.Graphics.FromImage(bitmap);
            using var font = Drawing.SystemFonts.CaptionFont ?? Drawing.SystemFonts.MessageBoxFont ?? WinForms.Control.DefaultFont;
            using var background = new Drawing.SolidBrush(Drawing.Color.FromArgb(160, Drawing.Color.Black));
            using var foreground = new Drawing.SolidBrush(Drawing.Color.White);

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

            var safeFont = font ?? WinForms.Control.DefaultFont;
            var safeText = text ?? string.Empty;
            var measured = graphics.MeasureString(safeText, safeFont);
            var rect = new Drawing.RectangleF(4, 4, measured.Width + 8, measured.Height + 4);
            graphics.FillRectangle(background, rect);
            graphics.DrawString(safeText, safeFont, foreground, new Drawing.PointF(rect.Left + 4, rect.Top + 2));
        }
        catch
        {
            // Debug overlay is best-effort only.
        }
    }
#endif

    private readonly struct StartStopScope : IDisposable
    {
        private readonly MonitorPreviewHost? _owner;

        public StartStopScope(MonitorPreviewHost owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            _owner?.ExitStartStop();
        }
    }

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
