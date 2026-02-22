using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;
using WinForms = System.Windows.Forms;
using Mieruka.App.Services.Ui;
using Mieruka.Core.Diagnostics;
using Mieruka.Core.Models;
using Mieruka.Core.Monitors;
using Serilog;

namespace Mieruka.App.Ui.PreviewBindings;

/// <summary>
/// Provides a reusable monitor preview control backed by <see cref="MonitorPreviewHost"/>.
/// </summary>
public sealed class MonitorPreviewDisplay : WinForms.UserControl
{
    private static readonly ILogger Logger = Log.ForContext<MonitorPreviewDisplay>();

    private readonly WinForms.PictureBox _pictureBox;
    private readonly List<SimRect> _simRects;
    private readonly List<(Drawing.RectangleF Bounds, string Text)> _glyphRegions;
    private readonly WinForms.ToolTip _tooltip;
    private readonly Queue<DateTime> _previewStartHistory = new();
    private bool _isStartingPreview;
    private bool _isStoppingPreview;
    private bool _previewStarted;
    private bool _previewRunning;
    private MonitorPreviewHost? _host;
    private MonitorInfo? _monitor;
    private MonitorCoordinateMapper? _coordinateMapper;
    private string? _currentGlyphTooltip;
    private string? _placeholderMessage;
    private int _paintExceptionCount;

    private const string AskGlyph = "❓";
    private const string AskTooltipText = "Solicitar confirmação antes de iniciar.";
    private const string NetworkGlyph = "🌐";
    private const string NetworkTooltipText = "Requer conexão de rede para iniciar.";
    private const int MinimumPreviewSurface = 50;
    private const int PreviewDepthLimit = 32;
    private const int PreviewLogicalDepthLimit = 32;
    private static readonly ThreadLocal<int> PreviewCallDepth = new(() => 0);
    private static readonly ThreadLocal<int> PreviewLogicalDepth = new(() => 0);

    internal readonly struct PreviewLogicalScope : IDisposable
    {
        private readonly ILogger _logger;
        private readonly bool _entered;

        public PreviewLogicalScope(string scope, ILogger logger)
        {
            _logger = logger;
            Scope = scope;
            var depth = PreviewLogicalDepth.Value + 1;
            if (depth > PreviewLogicalDepthLimit)
            {
                _entered = false;
                _logger.Warning(
                    "PreviewLogicalDepthLimitReached: scope={Scope} depth={Depth} limit={Limit} stack={Stack}",
                    Scope,
                    depth,
                    PreviewLogicalDepthLimit,
                    Environment.StackTrace);
            }
            else
            {
                PreviewLogicalDepth.Value = depth;
                _entered = true;
            }
        }

        public string Scope { get; }

        public bool Entered => _entered;

        public void Dispose()
        {
            if (_entered)
            {
                var depth = PreviewLogicalDepth.Value - 1;
                PreviewLogicalDepth.Value = Math.Max(0, depth);
            }
        }
    }

    internal readonly struct PreviewCallScope : IDisposable
    {
        private readonly ILogger _logger;
        private readonly bool _entered;

        public PreviewCallScope(string scope, ILogger logger)
        {
            Scope = scope;
            _logger = logger;
            var depth = PreviewCallDepth.Value + 1;
            if (depth > PreviewDepthLimit)
            {
                _entered = false;
                _logger.Warning(
                    "PreviewCallDepthLimitReached: scope={Scope} depth={Depth} limit={Limit} stack={Stack}",
                    Scope,
                    depth,
                    PreviewDepthLimit,
                    Environment.StackTrace);
            }
            else
            {
                PreviewCallDepth.Value = depth;
                _entered = true;
            }
        }

        public string Scope { get; }

        public bool Entered => _entered;

        public void Dispose()
        {
            if (_entered)
            {
                var depth = PreviewCallDepth.Value - 1;
                PreviewCallDepth.Value = Math.Max(0, depth);
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MonitorPreviewDisplay"/> class.
    /// </summary>
    public MonitorPreviewDisplay()
    {
        SetStyle(
            WinForms.ControlStyles.OptimizedDoubleBuffer
            | WinForms.ControlStyles.AllPaintingInWmPaint
            | WinForms.ControlStyles.UserPaint,
            true);
        DoubleBuffered = true;
        UpdateStyles();

        SuspendLayout();

        _pictureBox = new WinForms.PictureBox
        {
            Dock = WinForms.DockStyle.Fill,
            BackColor = Drawing.Color.FromArgb(176, 176, 176),
            SizeMode = WinForms.PictureBoxSizeMode.Zoom,
        };

        Controls.Add(_pictureBox);

        _simRects = new List<SimRect>();
        _glyphRegions = new List<(Drawing.RectangleF Bounds, string Text)>();
        _tooltip = ToolTipTamer.Create();

        _pictureBox.MouseDown += PictureBoxOnMouseDown;
        _pictureBox.MouseMove += PictureBoxOnMouseMove;
        _pictureBox.MouseLeave += PictureBoxOnMouseLeave;
        _pictureBox.Paint += PictureBoxOnPaint;

        ResumeLayout(false);
    }

    /// <inheritdoc />
    protected override WinForms.CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // NOTE: WS_EX_COMPOSITED (0x02000000) was removed.
            // It forces software rendering and conflicts with OptimizedDoubleBuffer,
            // causing double redraws and visible flickering/stuttering.
            // DoubleBuffered + AllPaintingInWmPaint is sufficient for flicker-free rendering.
            return cp;
        }
    }

    /// <summary>
    /// Occurs whenever the mouse moves inside the monitor preview.
    /// </summary>
    public event EventHandler<Drawing.Point>? MouseMovedInMonitorSpace;

    /// <summary>
    /// Occurs when the pointer leaves the preview area.
    /// </summary>
    public event EventHandler? MonitorMouseLeft;

    /// <summary>
    /// Occurs when the preview pipeline fails and is stopped.
    /// </summary>
    public event EventHandler<string>? PreviewFailed;

    /// <summary>
    /// Occurs when the preview starts running.
    /// </summary>
    public event EventHandler? PreviewStarted;

    /// <summary>
    /// Occurs when the preview stops running.
    /// </summary>
    public event EventHandler? PreviewStopped;

    /// <summary>
    /// Occurs when the mouse moves inside the preview using monitor coordinates.
    /// </summary>
    public event EventHandler<MonitorMouseEventArgs>? MonitorMouseMove;

    /// <summary>
    /// Occurs when the mouse is clicked inside the preview using monitor coordinates.
    /// </summary>
    public event EventHandler<MonitorMouseEventArgs>? MonitorMouseClick;

    /// <summary>
    /// Gets a value indicating whether user interactions should be ignored due to the host being paused or busy.
    /// </summary>
    public bool IsInteractionSuppressed
        => _host is { IsPaused: true } || _host is { IsBusy: true };

    /// <summary>
    /// Gets a value indicating whether the underlying host is currently paused.
    /// </summary>
    public bool IsPaused => _host?.IsPaused ?? false;

    public bool IsPreviewRunning => _previewRunning;

    /// <summary>
    /// Gets or sets the ambient edit session identifier used for logging correlation.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Guid? EditSessionId { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsCoordinateAnalysisMode { get; set; }

    public sealed class MonitorMouseEventArgs : EventArgs
    {
        public MonitorMouseEventArgs(string monitorId, Drawing.PointF monitorPoint, WinForms.MouseButtons button)
        {
            MonitorId = monitorId;
            MonitorPoint = monitorPoint;
            Button = button;
        }

        public string MonitorId { get; }

        public Drawing.PointF MonitorPoint { get; }

        public WinForms.MouseButtons Button { get; }
    }

    /// <summary>
    /// Represents a simulated rectangle drawn on top of the monitor preview.
    /// </summary>
    public sealed class SimRect
    {
        public Drawing.Rectangle MonRel { get; set; }

        public Drawing.Color Color { get; set; }

        public int Order { get; set; }

        public string? Title { get; set; }

        public bool RequiresNetwork { get; set; }

        public bool AskBefore { get; set; }
    }

    /// <summary>
    /// Binds the preview to the provided monitor.
    /// </summary>
    /// <param name="monitor">Monitor to display.</param>
    /// <param name="cadence">Optional frame cadence.</param>
    public async Task BindAsync(MonitorInfo monitor, bool autoStart = true, TimeSpan? cadence = null)
    {
        using var guard = new StackGuard(nameof(BindAsync));
        if (!guard.Entered)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(monitor);

        await UnbindAsync().ConfigureAwait(true);
        _monitor = monitor;
        RebuildCoordinateMapper(monitor);
        _previewStarted = false;
        _previewRunning = false;

        var monitorId = MonitorIdentifier.Create(monitor);
        var hostLogger = Log.ForContext<MonitorPreviewHost>();
        if (EditSessionId is Guid sessionId)
        {
            hostLogger = hostLogger.ForContext("EditSessionId", sessionId);
        }

        var host = new MonitorPreviewHost(monitorId, _pictureBox, hostLogger);

        if (cadence is TimeSpan cadenceValue)
        {
            host.FrameThrottle = cadenceValue < TimeSpan.Zero ? TimeSpan.Zero : cadenceValue;
        }

        _host = host;

        if (autoStart)
        {
            host.SetPreviewRequestedByUser(true);
            var started = await StartPreviewHostAsync(host, monitorId).ConfigureAwait(true);

            if (!started)
            {
                _monitor = null;
                if (string.IsNullOrWhiteSpace(_placeholderMessage))
                {
                    SetPlaceholder("Pré-visualização indisponível. Monitor configurado não foi encontrado.");
                }
            }
            else
            {
                TransitionToRunning();
                SetPlaceholder(null);
            }
        }
        else
        {
            host.SetPreviewRequestedByUser(false);
            SetPlaceholder(GetPreviewDisabledPlaceholder());
        }
    }

    private async Task<bool> StartPreviewHostAsync(MonitorPreviewHost host, string monitorId)
    {
        if (_monitor is { } monitor && !HasValidSurface(monitor))
        {
            Logger.Warning(
                "MonitorPreviewDisplay: monitor sem superfície válida para captura width={Width} height={Height} monitorId={MonitorId}",
                monitor.Bounds.Width,
                monitor.Bounds.Height,
                monitorId);
            SetPlaceholder("Monitor sem superfície válida para captura");
            _coordinateMapper = null;
            return false;
        }

        // The editor always prefers the BitBlt path to avoid GPU capture glitches in nested previews.
        var started = false;
        try
        {
            started = await host.StartSafeAsync(preferGpu: false).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "MonitorPreviewDisplay: failed to start preview host for {MonitorId}", monitorId);
            started = false;
            NotifyPreviewFailed("Falha ao iniciar a pré-visualização.");
        }

        return started;
    }

    public async Task EnsurePreviewStartedAsync()
    {
        using var logicalDepth = new PreviewLogicalScope(nameof(EnsurePreviewStartedAsync), Logger);
        if (!logicalDepth.Entered)
        {
            return;
        }

        using var depth = new PreviewCallScope(nameof(EnsurePreviewStartedAsync), Logger);
        if (!depth.Entered)
        {
            return;
        }

        using var guard = new StackGuard(nameof(EnsurePreviewStartedAsync));
        if (!guard.Entered)
        {
            return;
        }

        if (_isStartingPreview)
        {
            Logger.Debug("preview_start_rejected_reentrancy");
            return;
        }

        _isStartingPreview = true;
        try
        {
            var now = DateTime.UtcNow;
            while (_previewStartHistory.Count > 0 && (now - _previewStartHistory.Peek()) > TimeSpan.FromSeconds(10))
            {
                _previewStartHistory.Dequeue();
            }

            if (_previewStartHistory.Count >= 5)
            {
                Logger.Warning(
                    "preview_start_rate_limited: previewStartsLast10s={PreviewStartsLast10s}",
                    _previewStartHistory.Count);
                SetPlaceholder("Pré-visualização temporariamente bloqueada. Aguarde alguns segundos.");
                return;
            }

            _previewStartHistory.Enqueue(now);
            var monitorIdForLog = _monitor is null ? string.Empty : MonitorIdentifier.Create(_monitor);
            Logger.Debug(
                "MonitorPreviewDisplay: attempting to start preview for {MonitorId}",
                string.IsNullOrWhiteSpace(monitorIdForLog) ? "<unknown>" : monitorIdForLog);

            if (_previewStarted || _previewRunning)
            {
                return;
            }

            var host = _host;
            var monitorId = _monitor is null ? string.Empty : MonitorIdentifier.Create(_monitor);
            if (host is null || string.IsNullOrWhiteSpace(monitorId))
            {
                NotifyPreviewFailed("Pré-visualização indisponível.");
                return;
            }

            host.SetPreviewRequestedByUser(true);
            var started = await StartPreviewHostAsync(host, monitorId).ConfigureAwait(true);
            if (started)
            {
                TransitionToRunning();
                SetPlaceholder(null);
            }
            else
            {
                _monitor = null;
                var placeholder = GetUnavailablePlaceholder();
                NotifyPreviewFailed(placeholder);
                if (string.IsNullOrWhiteSpace(_placeholderMessage))
                {
                    SetPlaceholder(placeholder);
                }
            }
        }
        finally
        {
            _isStartingPreview = false;
        }
    }

    public async Task UnbindAsync(CancellationToken cancellationToken = default)
    {
        using var guard = new StackGuard(nameof(UnbindAsync));
        if (!guard.Entered)
        {
            return;
        }

        var host = _host;
        _host = null;
        _monitor = null;
        _coordinateMapper = null;
        var wasRunning = _previewRunning;
        _previewStarted = false;
        _previewRunning = false;

        SetPlaceholder(null);

        if (host is null)
        {
            return;
        }

        try
        {
            host.SetPreviewRequestedByUser(false);
            await host.StopSafeAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Ignore preview stop failures.
        }

        host.Dispose();

        _pictureBox.Image = null;
        SetSimulationRects(Array.Empty<SimRect>());

        if (wasRunning)
        {
            PreviewStopped?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task SuspendCaptureAsync(CancellationToken cancellationToken = default)
    {
        using var guard = new StackGuard(nameof(SuspendCaptureAsync));
        if (!guard.Entered)
        {
            return;
        }

        if (_host is { } host)
        {
            await host.SuspendCaptureAsync(cancellationToken).ConfigureAwait(true);
        }
    }

    public async Task ResumeCaptureAsync(CancellationToken cancellationToken = default)
    {
        using var guard = new StackGuard(nameof(ResumeCaptureAsync));
        if (!guard.Entered)
        {
            return;
        }

        if (_host is { } host)
        {
            await Task.Run(() => host.ResumeCapture(), cancellationToken).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Pauses frame processing while keeping the current frame visible.
    /// </summary>
    public void Pause()
    {
        // Fire-and-forget: starts PauseAsync on the UI thread (no ConfigureAwait(false) at
        // entry), so its continuation will also resume on the UI thread once it is free.
        // Never use .GetAwaiter().GetResult() here — it would deadlock when called from the
        // UI thread because PauseAsync needs the UI thread for its own continuation.
        _ = PauseAsync();
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        using var guard = new StackGuard(nameof(PauseAsync));
        if (!guard.Entered)
        {
            return;
        }

        if (_host is { } host)
        {
            host.SetEditorPreviewDisabledMode(true);
            await host.PauseAsync(cancellationToken).ConfigureAwait(true);
        }

        SetPlaceholder(GetPreviewDisabledPlaceholder());
    }

    /// <summary>
    /// Resumes frame processing after a pause.
    /// </summary>
    public void Resume()
    {
        // Same rationale as Pause(): fire-and-forget to prevent UI thread deadlock.
        _ = ResumeAsync();
    }

    public async Task ResumeAsync()
    {
        using var guard = new StackGuard(nameof(ResumeAsync));
        if (!guard.Entered)
        {
            return;
        }

        if (_host is { } host)
        {
            host.SetEditorPreviewDisabledMode(false);
            await host.ResumeAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Updates whether the underlying preview host should process frames based on visibility.
    /// </summary>
    /// <param name="isVisible">True when the preview is visible.</param>
    public void SetPreviewVisibility(bool isVisible)
    {
        using var guard = new StackGuard(nameof(SetPreviewVisibility));
        if (!guard.Entered)
        {
            return;
        }

        if (_host is { } host)
        {
            host.IsVisible = isVisible;
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Unbind();
            _pictureBox.MouseDown -= PictureBoxOnMouseDown;
            _pictureBox.MouseMove -= PictureBoxOnMouseMove;
            _pictureBox.MouseLeave -= PictureBoxOnMouseLeave;
            _pictureBox.Paint -= PictureBoxOnPaint;
            _pictureBox.Dispose();
            _tooltip.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Unbind()
    {
        // This runs on the UI thread (called from Dispose). We cannot block on UnbindAsync()
        // because UnbindAsync uses ConfigureAwait(true) which needs the UI thread for its
        // continuation — blocking it would deadlock.
        //
        // Solution: replicate synchronous state cleanup here, then fire-and-forget the async
        // stop on the thread pool so the UI thread is never blocked.

        using var guard = new StackGuard(nameof(UnbindAsync));
        if (!guard.Entered)
        {
            return;
        }

        var host = _host;
        _host = null;
        _monitor = null;
        _coordinateMapper = null;
        var wasRunning = _previewRunning;
        _previewStarted = false;
        _previewRunning = false;

        SetPlaceholder(null);
        _pictureBox.Image = null;
        SetSimulationRects(Array.Empty<SimRect>());

        if (host is not null)
        {
            host.SetPreviewRequestedByUser(false);
            // Stop + dispose async on the thread pool — no sync context is captured.
            _ = Task.Run(async () =>
            {
                try
                {
                    await host.StopSafeAsync(default).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logger.Debug(ex, "Stop failed during unbind disposal.");
                }
                finally
                {
                    host.Dispose();
                }
            });
        }

        if (wasRunning)
        {
            PreviewStopped?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Applies a set of simulated rectangles to the preview overlay.
    /// </summary>
    /// <param name="rectangles">Rectangles to display.</param>
    public void SetSimulationRects(IEnumerable<SimRect>? rectangles)
    {
        using var guard = new StackGuard(nameof(SetSimulationRects));
        if (!guard.Entered)
        {
            return;
        }

        var next = NormalizeRectangles(rectangles);

        if (AreEquivalent(_simRects, next))
        {
            return;
        }

        _simRects.Clear();
        _simRects.AddRange(next);

        _glyphRegions.Clear();
        ClearGlyphTooltip();
        _pictureBox.Invalidate();
    }

    private static List<SimRect> NormalizeRectangles(IEnumerable<SimRect>? rectangles)
    {
        var result = new List<SimRect>();

        if (rectangles is null)
        {
            return result;
        }

        foreach (var rectangle in rectangles)
        {
            if (rectangle is null)
            {
                continue;
            }

            result.Add(new SimRect
            {
                MonRel = rectangle.MonRel,
                Color = rectangle.Color,
                Order = rectangle.Order,
                Title = rectangle.Title,
                RequiresNetwork = rectangle.RequiresNetwork,
                AskBefore = rectangle.AskBefore,
            });
        }

        return result;
    }

    private static bool AreEquivalent(IReadOnlyList<SimRect> current, IReadOnlyList<SimRect> next)
    {
        if (current.Count != next.Count)
        {
            return false;
        }

        for (var i = 0; i < current.Count; i++)
        {
            var existing = current[i];
            var candidate = next[i];

            if (existing.MonRel != candidate.MonRel)
            {
                return false;
            }

            if (existing.Color.ToArgb() != candidate.Color.ToArgb())
            {
                return false;
            }

            if (existing.Order != candidate.Order)
            {
                return false;
            }

            if (!string.Equals(existing.Title, candidate.Title, StringComparison.Ordinal))
            {
                return false;
            }

            if (existing.RequiresNetwork != candidate.RequiresNetwork)
            {
                return false;
            }

            if (existing.AskBefore != candidate.AskBefore)
            {
                return false;
            }
        }

        return true;
    }

    private void PictureBoxOnMouseDown(object? sender, WinForms.MouseEventArgs e)
    {
        using var guard = new StackGuard(nameof(PictureBoxOnMouseDown));
        if (!guard.Entered)
        {
            Logger.Debug("MonitorPreviewDisplay: ignoring click, stack guard not entered");
            return;
        }

        if (_isStartingPreview || _isStoppingPreview)
        {
            Logger.Debug("MonitorPreviewDisplay: ignoring click while transition in progress");
            return;
        }

        if (e.Button != WinForms.MouseButtons.Left)
        {
            Logger.Debug("MonitorPreviewDisplay: ignoring non-left click");
            return;
        }

        if (TryClientToMonitor(e.Location, out var monitorPoint))
        {
            MonitorMouseClick?.Invoke(
                this,
                new MonitorMouseEventArgs(
                    _monitor is null ? string.Empty : MonitorIdentifier.Create(_monitor),
                    monitorPoint,
                    e.Button));
        }
    }

    public async Task StopPreviewAsync(CancellationToken cancellationToken = default)
    {
        using var logicalDepth = new PreviewLogicalScope(nameof(StopPreviewAsync), Logger);
        if (!logicalDepth.Entered)
        {
            return;
        }

        using var depth = new PreviewCallScope(nameof(StopPreviewAsync), Logger);
        if (!depth.Entered)
        {
            return;
        }

        using var guard = new StackGuard(nameof(StopPreviewAsync));
        if (!guard.Entered)
        {
            return;
        }

        if (_isStoppingPreview)
        {
            Logger.Debug("preview_stop_rejected_reentrancy");
            return;
        }

        _isStoppingPreview = true;
        try
        {
            if (!_previewRunning && !_previewStarted)
            {
                return;
            }

            var host = _host;
            if (host is not null)
            {
                try
                {
                    await host.StopSafeAsync(cancellationToken).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    Logger.Warning(ex, "MonitorPreviewDisplay: failed to stop preview host safely");
                    NotifyPreviewFailed("Falha ao encerrar a pré-visualização.");
                }
            }

            if (_pictureBox.IsDisposed)
            {
                TransitionToStopped();
                return;
            }

            try
            {
                _pictureBox.Image?.Dispose();
            }
            catch
            {
            }

            _pictureBox.Image = null;
            SetPlaceholder("Pré-visualização pausada — clique para iniciar");
            TransitionToStopped();
        }
        finally
        {
            _isStoppingPreview = false;
        }
    }

    private void NotifyPreviewFailed(string reason)
    {
        TransitionToStopped();
        PreviewFailed?.Invoke(this, reason);
        if (!_pictureBox.IsDisposed)
        {
            SetPlaceholder(reason);
        }
    }

    private void PictureBoxOnMouseMove(object? sender, WinForms.MouseEventArgs e)
    {
        using var guard = new StackGuard(nameof(PictureBoxOnMouseMove));
        if (!guard.Entered)
        {
            return;
        }

        UpdateGlyphTooltip(e.Location);

        if (!TryClientToMonitor(e.Location, out var monitorPoint))
        {
            return;
        }

        MouseMovedInMonitorSpace?.Invoke(this, Drawing.Point.Round(monitorPoint));
        MonitorMouseMove?.Invoke(
            this,
            new MonitorMouseEventArgs(
                _monitor is null ? string.Empty : MonitorIdentifier.Create(_monitor),
                monitorPoint,
                e.Button));
    }

    private void TransitionToStopped()
    {
        var wasRunning = _previewRunning;
        _previewRunning = false;
        _previewStarted = false;

        if (wasRunning)
        {
            PreviewStopped?.Invoke(this, EventArgs.Empty);
        }
    }

    private void PictureBoxOnMouseLeave(object? sender, EventArgs e)
    {
        using var guard = new StackGuard(nameof(PictureBoxOnMouseLeave));
        if (!guard.Entered)
        {
            return;
        }

        ClearGlyphTooltip();
        MonitorMouseLeft?.Invoke(this, EventArgs.Empty);
    }

    private void PictureBoxOnPaint(object? sender, WinForms.PaintEventArgs e)
    {
        using var guard = new StackGuard(nameof(PictureBoxOnPaint));
        if (!guard.Entered)
        {
            return;
        }

        try
        {
            if (!string.IsNullOrEmpty(_placeholderMessage))
            {
                DrawPlaceholder(e.Graphics, _placeholderMessage);
                _glyphRegions.Clear();
                return;
            }

            var mapper = _coordinateMapper;
            if (mapper is null || _simRects.Count == 0)
            {
                _glyphRegions.Clear();
                return;
            }

            var previewResolution = mapper.PreviewResolution;
            if (!previewResolution.HasValidSize)
            {
                _glyphRegions.Clear();
                return;
            }

            var displayRect = GetImageDisplayRectangle(_pictureBox);
            if (displayRect.Width <= 0 || displayRect.Height <= 0)
            {
                _glyphRegions.Clear();
                return;
            }

            var graphics = e.Graphics;
            graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = Drawing2D.PixelOffsetMode.HighQuality;

            _glyphRegions.Clear();

            foreach (var rect in _simRects)
            {
                if (rect.MonRel.Width <= 0 || rect.MonRel.Height <= 0)
                {
                    continue;
                }

                var previewRect = mapper.MonitorToPreview(rect.MonRel);
                var canvasRect = mapper.PreviewToUi(previewRect, displayRect);

                if (canvasRect.Width <= 0f || canvasRect.Height <= 0f)
                {
                    continue;
                }

                var baseColor = rect.Color;
                if (baseColor.A == 0)
                {
                    baseColor = Drawing.Color.DodgerBlue;
                }

                using (var fill = new Drawing.SolidBrush(Drawing.Color.FromArgb(96, baseColor)))
                {
                    graphics.FillRectangle(fill, canvasRect);
                }

                var borderColor = Drawing.Color.FromArgb(220, baseColor);
                using (var border = new Drawing.Pen(borderColor, 2f))
                {
                    graphics.DrawRectangle(border, canvasRect.X, canvasRect.Y, canvasRect.Width, canvasRect.Height);
                }

                DrawLabel(graphics, rect, canvasRect);
                DrawIndicatorGlyphs(graphics, rect, canvasRect, borderColor);
            }
        }
        catch (Exception ex)
        {
            var count = Interlocked.Increment(ref _paintExceptionCount);
            if (count <= 3 || count % 50 == 0)
            {
                Logger.Error(ex, "Paint exception (count={Count})", count);
            }
        }
    }

    private void DrawPlaceholder(Drawing.Graphics graphics, string message)
    {
        var bounds = _pictureBox.ClientRectangle;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var text = string.IsNullOrWhiteSpace(message) ? string.Empty : message.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var marginX = Math.Min(32, bounds.Width / 8);
        var marginY = Math.Min(32, bounds.Height / 8);
        var layout = Drawing.Rectangle.Inflate(bounds, -marginX, -marginY);
        if (layout.Width <= 0 || layout.Height <= 0)
        {
            layout = bounds;
        }

        using var background = new Drawing.SolidBrush(Drawing.Color.FromArgb(210, Drawing.Color.White));
        using var border = new Drawing.Pen(Drawing.Color.FromArgb(160, 0, 0, 0), 1.5f);
        graphics.FillRectangle(background, layout);
        graphics.DrawRectangle(border, layout);

        var font = Font ?? WinForms.Control.DefaultFont;
        WinForms.TextRenderer.DrawText(
            graphics,
            text,
            font,
            layout,
            Drawing.Color.Black,
            WinForms.TextFormatFlags.HorizontalCenter | WinForms.TextFormatFlags.VerticalCenter | WinForms.TextFormatFlags.WordBreak);
    }

    private void DrawLabel(Drawing.Graphics graphics, SimRect rect, Drawing.RectangleF canvasRect)
    {
        var label = FormatLabel(rect);
        if (string.IsNullOrWhiteSpace(label))
        {
            return;
        }

        var labelSize = WinForms.TextRenderer.MeasureText(label, Font, Drawing.Size.Empty, WinForms.TextFormatFlags.NoPadding);
        var labelRect = new Drawing.Rectangle(
            (int)Math.Round(canvasRect.X + 4f),
            (int)Math.Round(canvasRect.Y + 4f),
            Math.Max(labelSize.Width + 8, 0),
            Math.Max(labelSize.Height + 4, 0));

        if (labelRect.Width <= 0 || labelRect.Height <= 0)
        {
            return;
        }

        using var labelBrush = new Drawing.SolidBrush(Drawing.Color.FromArgb(180, 0, 0, 0));
        graphics.FillRectangle(labelBrush, labelRect);
        WinForms.TextRenderer.DrawText(
            graphics,
            label,
            Font,
            labelRect,
            Drawing.Color.White,
            WinForms.TextFormatFlags.Left | WinForms.TextFormatFlags.VerticalCenter | WinForms.TextFormatFlags.NoPadding);
    }

    private void DrawIndicatorGlyphs(Drawing.Graphics graphics, SimRect rect, Drawing.RectangleF canvasRect, Drawing.Color borderColor)
    {
        var rightEdge = canvasRect.Right - 4f;

        void DrawGlyph(string glyph, string tooltip)
        {
            if (string.IsNullOrEmpty(glyph))
            {
                return;
            }

            var glyphSize = WinForms.TextRenderer.MeasureText(glyph, Font, Drawing.Size.Empty, WinForms.TextFormatFlags.NoPadding);
            var glyphRect = new Drawing.Rectangle(
                (int)Math.Round(rightEdge - glyphSize.Width - 6f),
                (int)Math.Round(canvasRect.Y + 4f),
                glyphSize.Width + 6,
                glyphSize.Height + 6);

            if (glyphRect.Width <= 0 || glyphRect.Height <= 0)
            {
                return;
            }

            using var glyphFill = new Drawing.SolidBrush(Drawing.Color.FromArgb(235, 255, 255, 255));
            graphics.FillEllipse(glyphFill, glyphRect);

            using var glyphBorder = new Drawing.Pen(borderColor, 1.5f);
            graphics.DrawEllipse(glyphBorder, glyphRect);

            WinForms.TextRenderer.DrawText(
                graphics,
                glyph,
                Font,
                glyphRect,
                Drawing.Color.Black,
                WinForms.TextFormatFlags.HorizontalCenter | WinForms.TextFormatFlags.VerticalCenter | WinForms.TextFormatFlags.NoPadding);

            _glyphRegions.Add((new Drawing.RectangleF(glyphRect.X, glyphRect.Y, glyphRect.Width, glyphRect.Height), tooltip));
            rightEdge = glyphRect.X - 4f;
        }

        if (rect.AskBefore)
        {
            DrawGlyph(AskGlyph, AskTooltipText);
        }

        if (rect.RequiresNetwork)
        {
            DrawGlyph(NetworkGlyph, NetworkTooltipText);
        }
    }

    private static string FormatLabel(SimRect rect)
    {
        var title = rect.Title?.Trim() ?? string.Empty;
        if (rect.Order <= 0)
        {
            return title;
        }

        if (string.IsNullOrEmpty(title))
        {
            return rect.Order.ToString(CultureInfo.InvariantCulture);
        }

        var orderText = rect.Order.ToString(CultureInfo.InvariantCulture);
        return string.Concat(orderText, ". ", title);
    }

    private static Drawing.RectangleF GetImageDisplayRectangle(WinForms.PictureBox pictureBox)
    {
        var image = TryGetCurrentImage(pictureBox);
        if (!IsValidImage(image))
        {
            return Drawing.RectangleF.Empty;
        }

        if (!TryGetImageSize(image, out var imageWidth, out var imageHeight))
        {
            return Drawing.RectangleF.Empty;
        }

        return pictureBox.SizeMode switch
        {
            WinForms.PictureBoxSizeMode.Normal or WinForms.PictureBoxSizeMode.AutoSize => new Drawing.RectangleF(0, 0, imageWidth, imageHeight),
            WinForms.PictureBoxSizeMode.StretchImage => new Drawing.RectangleF(0, 0, pictureBox.ClientSize.Width, pictureBox.ClientSize.Height),
            WinForms.PictureBoxSizeMode.CenterImage => new Drawing.RectangleF(
                (pictureBox.ClientSize.Width - imageWidth) / 2f,
                (pictureBox.ClientSize.Height - imageHeight) / 2f,
                imageWidth,
                imageHeight),
            WinForms.PictureBoxSizeMode.Zoom => CalculateZoomRectangle(pictureBox, image!, imageWidth, imageHeight),
            _ => new Drawing.RectangleF(0, 0, pictureBox.ClientSize.Width, pictureBox.ClientSize.Height),
        };
    }

    private static Drawing.RectangleF CalculateZoomRectangle(WinForms.PictureBox pictureBox, Drawing.Image image, int imageWidth, int imageHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0)
        {
            return Drawing.RectangleF.Empty;
        }

        var clientSize = pictureBox.ClientSize;
        if (clientSize.Width <= 0 || clientSize.Height <= 0)
        {
            return Drawing.RectangleF.Empty;
        }

        var ratio = Math.Min((float)clientSize.Width / imageWidth, (float)clientSize.Height / imageHeight);
        var width = imageWidth * ratio;
        var height = imageHeight * ratio;
        var x = (clientSize.Width - width) / 2f;
        var y = (clientSize.Height - height) / 2f;
        return new Drawing.RectangleF(x, y, width, height);
    }

    public Drawing.PointF ClientToMonitor(Drawing.Point clientPoint)
    {
        return TryClientToMonitor(clientPoint, out var monitorPoint) ? monitorPoint : Drawing.PointF.Empty;
    }

    public bool TryClientToMonitor(Drawing.Point clientPoint, out Drawing.PointF monitorPoint)
    {
        monitorPoint = Drawing.PointF.Empty;

        var mapper = _coordinateMapper;
        if (mapper is null)
        {
            return false;
        }

        var displayRect = GetDisplayRectangleForMapping(_pictureBox);
        if (displayRect.Width <= 0 || displayRect.Height <= 0 || !displayRect.Contains(clientPoint))
        {
            return false;
        }

        monitorPoint = mapper.UiToMonitor(clientPoint, displayRect);
        return true;
    }

    private static Drawing.RectangleF GetDisplayRectangleForMapping(WinForms.PictureBox pictureBox)
    {
        var displayRect = GetImageDisplayRectangle(pictureBox);
        if (displayRect.Width > 0 && displayRect.Height > 0)
        {
            return displayRect;
        }

        var clientRect = pictureBox.ClientRectangle;
        return clientRect.Width > 0 && clientRect.Height > 0
            ? new Drawing.RectangleF(clientRect.X, clientRect.Y, clientRect.Width, clientRect.Height)
            : Drawing.RectangleF.Empty;
    }

    private static bool TryGetImageSize(Drawing.Image? image, out int width, out int height)
    {
        if (image is null)
        {
            width = 0;
            height = 0;
            return false;
        }

        try
        {
            width = image.Width;
            height = image.Height;
            return width > 0 && height > 0;
        }
        catch (Exception ex) when (ex is ArgumentException or ObjectDisposedException or ExternalException)
        {
            Logger.Debug(ex, "Dimensões inválidas ao calcular retângulo de imagem.");
            width = 0;
            height = 0;
            return false;
        }
    }

    private void UpdateGlyphTooltip(Drawing.Point location)
    {
        string? tooltip = null;

        foreach (var entry in _glyphRegions)
        {
            if (entry.Bounds.Contains(location))
            {
                tooltip = entry.Text;
                break;
            }
        }

        if (!string.Equals(tooltip, _currentGlyphTooltip, StringComparison.Ordinal))
        {
            _tooltip.SetToolTip(_pictureBox, tooltip);
            _currentGlyphTooltip = tooltip;
        }
    }

    private void ClearGlyphTooltip()
    {
        if (_currentGlyphTooltip is not null)
        {
            _tooltip.SetToolTip(_pictureBox, null);
            _currentGlyphTooltip = null;
        }
    }

    private static Drawing.Image? TryGetCurrentImage(WinForms.PictureBox pictureBox)
    {
        try
        {
            return pictureBox.Image;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            Logger.Debug(ex, "Falha ao obter imagem atual do preview.");
            return null;
        }
    }

    private static bool IsValidImage(Drawing.Image? image)
    {
        if (image is null)
        {
            return false;
        }

        try
        {
            return image.Width > 0 && image.Height > 0;
        }
        catch (Exception ex) when (ex is ArgumentException or ObjectDisposedException or ExternalException)
        {
            Logger.Debug(ex, "Imagem inválida ao inspecionar preview.");
            return false;
        }
    }

    private void RebuildCoordinateMapper(MonitorInfo monitor)
    {
        if (monitor.Bounds.Width < MinimumPreviewSurface || monitor.Bounds.Height < MinimumPreviewSurface)
        {
            _coordinateMapper = null;
            Logger.Debug(
                "MonitorPreviewDisplay: coordinate mapper disabled for monitorId={MonitorId} width={Width} height={Height}",
                MonitorIdentifier.Create(monitor),
                monitor.Bounds.Width,
                monitor.Bounds.Height);
            return;
        }

        _coordinateMapper = new MonitorCoordinateMapper(monitor);
        Logger.Debug(
            "MonitorPreviewDisplay: coordinate mapper rebuilt for monitorId={MonitorId} bounds={Bounds}",
            MonitorIdentifier.Create(monitor),
            monitor.Bounds);
    }

    private void TransitionToRunning()
    {
        var wasRunning = _previewRunning;
        _previewStarted = true;
        _previewRunning = true;

        if (!wasRunning)
        {
            PreviewStarted?.Invoke(this, EventArgs.Empty);
        }
    }

    private static bool HasValidSurface(MonitorInfo monitor)
    {
        if (monitor.Bounds.Width >= MinimumPreviewSurface && monitor.Bounds.Height >= MinimumPreviewSurface)
        {
            return true;
        }

        return monitor.Width >= MinimumPreviewSurface && monitor.Height >= MinimumPreviewSurface;
    }

    private void SetPlaceholder(string? message)
    {
        var trimmed = string.IsNullOrWhiteSpace(message) ? null : message.Trim();
        _placeholderMessage = trimmed;

        if (trimmed is not null)
        {
            ClearGlyphTooltip();
            _pictureBox.Image = null;
        }

        _pictureBox.Invalidate();
    }

    private string GetPreviewDisabledPlaceholder()
    {
        return IsCoordinateAnalysisMode
            ? "Análise de coordenadas do monitor selecionado (preview desativado)"
            : "Pré-visualização pausada — clique para iniciar";
    }

    private string GetUnavailablePlaceholder()
    {
        return IsCoordinateAnalysisMode
            ? "Análise de coordenadas do monitor selecionado (preview desativado)"
            : "Pré-visualização indisponível. Monitor configurado não foi encontrado.";
    }
}
