using System;
using System.Collections.Generic;
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
    private MonitorPreviewHost? _host;
    private MonitorInfo? _monitor;
    private string? _currentGlyphTooltip;
    private string? _placeholderMessage;
    private int _paintExceptionCount;

    private const string AskGlyph = "❓";
    private const string AskTooltipText = "Solicitar confirmação antes de iniciar.";
    private const string NetworkGlyph = "🌐";
    private const string NetworkTooltipText = "Requer conexão de rede para iniciar.";

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
            cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED
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
    /// Gets a value indicating whether user interactions should be ignored due to the host being paused or busy.
    /// </summary>
    public bool IsInteractionSuppressed
        => _host is { IsPaused: true } || _host is { IsBusy: true };

    /// <summary>
    /// Gets a value indicating whether the underlying host is currently paused.
    /// </summary>
    public bool IsPaused => _host?.IsPaused ?? false;

    /// <summary>
    /// Gets or sets the ambient edit session identifier used for logging correlation.
    /// </summary>
    public Guid? EditSessionId { get; set; }

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
    public async void Bind(MonitorInfo monitor, TimeSpan? cadence = null)
    {
        using var guard = new StackGuard(nameof(Bind));
        if (!guard.Entered)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(monitor);

        await UnbindAsync().ConfigureAwait(true);
        _monitor = monitor;

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

        // The editor always prefers the BitBlt path to avoid GPU capture glitches in nested previews.
        var started = false;
        try
        {
            started = await host.StartSafeAsync(preferGpu: false).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "MonitorPreviewDisplay.Bind: failed to start preview host for {MonitorId}", monitorId);
            started = false;
        }

        _host = host;

        if (!started)
        {
            _monitor = null;
            SetPlaceholder("Pré-visualização indisponível. Monitor configurado não foi encontrado.");
        }
        else
        {
            SetPlaceholder(null);
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

        SetPlaceholder(null);

        if (host is null)
        {
            return;
        }

        try
        {
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
        => PauseAsync().GetAwaiter().GetResult();

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        using var guard = new StackGuard(nameof(PauseAsync));
        if (!guard.Entered)
        {
            return;
        }

        if (_host is { } host)
        {
            await host.PauseAsync(cancellationToken).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Resumes frame processing after a pause.
    /// </summary>
    public void Resume()
        => ResumeAsync().GetAwaiter().GetResult();

    public async Task ResumeAsync()
    {
        using var guard = new StackGuard(nameof(ResumeAsync));
        if (!guard.Entered)
        {
            return;
        }

        if (_host is { } host)
        {
            await host.ResumeAsync().ConfigureAwait(true);
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Unbind();
            _pictureBox.MouseMove -= PictureBoxOnMouseMove;
            _pictureBox.MouseLeave -= PictureBoxOnMouseLeave;
            _pictureBox.Paint -= PictureBoxOnPaint;
            _pictureBox.Dispose();
            _tooltip.Dispose();
        }

        base.Dispose(disposing);
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

    private void PictureBoxOnMouseMove(object? sender, WinForms.MouseEventArgs e)
    {
        using var guard = new StackGuard(nameof(PictureBoxOnMouseMove));
        if (!guard.Entered)
        {
            return;
        }

        UpdateGlyphTooltip(e.Location);

        var monitor = _monitor;
        if (monitor is null)
        {
            return;
        }

        var displayRect = GetImageDisplayRectangle(_pictureBox);
        if (displayRect.Width <= 0 || displayRect.Height <= 0 || !displayRect.Contains(e.Location))
        {
            return;
        }

        var relativeX = (e.X - displayRect.X) / displayRect.Width;
        var relativeY = (e.Y - displayRect.Y) / displayRect.Height;

        relativeX = Math.Clamp(relativeX, 0f, 1f);
        relativeY = Math.Clamp(relativeY, 0f, 1f);

        var bounds = monitor.Bounds;
        var monitorX = (int)Math.Round(relativeX * bounds.Width, MidpointRounding.AwayFromZero);
        var monitorY = (int)Math.Round(relativeY * bounds.Height, MidpointRounding.AwayFromZero);

        monitorX = Math.Clamp(monitorX, 0, Math.Max(0, bounds.Width));
        monitorY = Math.Clamp(monitorY, 0, Math.Max(0, bounds.Height));

        MouseMovedInMonitorSpace?.Invoke(this, new Drawing.Point(monitorX, monitorY));
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

            if (_monitor is null || _simRects.Count == 0)
            {
                _glyphRegions.Clear();
                return;
            }

            var monitorWidth = _monitor.Width > 0 ? _monitor.Width : _monitor.Bounds.Width;
            var monitorHeight = _monitor.Height > 0 ? _monitor.Height : _monitor.Bounds.Height;
            if (monitorWidth <= 0 || monitorHeight <= 0)
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

            var scaleX = displayRect.Width / monitorWidth;
            var scaleY = displayRect.Height / monitorHeight;

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

                var canvasRect = new Drawing.RectangleF(
                    displayRect.X + (rect.MonRel.X * scaleX),
                    displayRect.Y + (rect.MonRel.Y * scaleY),
                    rect.MonRel.Width * scaleX,
                    rect.MonRel.Height * scaleY);

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
}
