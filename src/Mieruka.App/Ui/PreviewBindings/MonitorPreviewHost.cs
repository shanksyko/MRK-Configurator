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
    private static readonly ILogger StaticLogger = Log.ForContext<MonitorPreviewHost>();
    private static readonly TimeSpan DefaultFrameThrottle = PreviewSettings.CalculateFrameInterval(PreviewSettings.Default.TargetFpsGdi);
    private static readonly TimeSpan GdiFrameThrottle = PreviewSettings.Default.GetGdiFrameInterval();
    private static readonly TimeSpan GpuFrameThrottle = PreviewSettings.Default.GetGpuFrameInterval();
    private static readonly TimeSpan SafeModeFrameThrottle = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan SafeModeStartDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan LifecycleWaitDelay = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan GpuFallbackLogInterval = TimeSpan.FromSeconds(10);
    private static long _lastGpuFallbackLogTicks;
    private const int MinimumCaptureSurface = 50;

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
    private const int PendingFrameLimit = 4;
    private readonly Guid _previewSessionId = Guid.NewGuid();
    private Drawing.Bitmap? _previewPlaceholderBitmap;
    private bool _editorPreviewDisabledMode;
    private readonly EventHandler _frameAnimationHandler;
    private TimeSpan _frameThrottle = DefaultFrameThrottle;
    private long _frameThrottleTicks = DefaultFrameThrottle.Ticks; // lock-free hot path
    private long _nextFrameAtTicks;                                 // lock-free hot path
    private bool _frameThrottleCustomized;
    private int _isVisible = 1;
    private Drawing.Bitmap? _currentFrame;
    private Drawing.Bitmap? _editorSnapshotBitmap;
    private bool _editorSnapshotModeEnabled;
    private bool _previewRequestedByUser;
    private bool _previewRunning;
    private bool _previewPaused;
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
    private Drawing.Rectangle _lastSelectionBounds = Drawing.Rectangle.Empty;
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
    /// Gets a value indicating whether the preview was explicitly requested by the user.
    /// </summary>
    public bool PreviewRequestedByUser => Volatile.Read(ref _previewRequestedByUser);

    /// <summary>
    /// Gets a value indicating whether the preview pipeline is running.
    /// </summary>
    public bool IsPreviewRunning => Volatile.Read(ref _previewRunning);

    /// <summary>
    /// Gets a value indicating whether the preview was paused by the user.
    /// </summary>
    public bool IsUserPaused => Volatile.Read(ref _previewPaused);

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
                "EditorPreviewDisabled: usando imagem placeholder para monitor={MonitorId}",
                MonitorId);
            ApplyPlaceholderFrame();
        }
        else
        {
            _logger.Information(
                "EditorPreviewDisabled: saindo do modo placeholder, preview real pode ser retomado");
        }
    }

    public void SetPreviewRequestedByUser(bool requested)
    {
        Volatile.Write(ref _previewRequestedByUser, requested);

        if (!requested)
        {
            Volatile.Write(ref _previewRunning, false);
            Volatile.Write(ref _previewPaused, false);
        }
        else
        {
            Volatile.Write(ref _previewPaused, false);
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
                Volatile.Write(ref _frameThrottleTicks, sanitized.Ticks);
                if (_frameThrottle <= TimeSpan.Zero)
                {
                    _nextFrameAt = DateTime.MinValue;
                    Volatile.Write(ref _nextFrameAtTicks, 0L);
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
                        Volatile.Write(ref _frameThrottleTicks, SafeModeFrameThrottle.Ticks);
                        _nextFrameAt = DateTime.MinValue;
                        Volatile.Write(ref _nextFrameAtTicks, 0L);
                    }
                }
            }
        }
    }

}
