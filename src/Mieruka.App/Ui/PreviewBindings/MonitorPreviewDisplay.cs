using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Mieruka.App.Services.Ui;
using Mieruka.Core.Models;
using Serilog;

namespace Mieruka.App.Ui.PreviewBindings;

/// <summary>
/// Provides a reusable monitor preview control backed by <see cref="MonitorPreviewHost"/>.
/// </summary>
public sealed class MonitorPreviewDisplay : UserControl
{
    private static readonly ILogger Logger = Log.ForContext<MonitorPreviewDisplay>();

    private readonly PictureBox _pictureBox;
    private readonly List<SimRect> _simRects;
    private readonly List<(RectangleF Bounds, string Text)> _glyphRegions;
    private readonly ToolTip _tooltip;
    private MonitorPreviewHost? _host;
    private MonitorInfo? _monitor;
    private string? _currentGlyphTooltip;
    private string? _placeholderMessage;

    private const string AskGlyph = "❓";
    private const string AskTooltipText = "Solicitar confirmação antes de iniciar.";
    private const string NetworkGlyph = "🌐";
    private const string NetworkTooltipText = "Requer conexão de rede para iniciar.";

    /// <summary>
    /// Initializes a new instance of the <see cref="MonitorPreviewDisplay"/> class.
    /// </summary>
    public MonitorPreviewDisplay()
    {
        SuspendLayout();

        _pictureBox = new PictureBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(176, 176, 176),
            SizeMode = PictureBoxSizeMode.Zoom,
        };

        Controls.Add(_pictureBox);

        _simRects = new List<SimRect>();
        _glyphRegions = new List<(RectangleF Bounds, string Text)>();
        _tooltip = ToolTipTamer.Create();

        _pictureBox.MouseMove += PictureBoxOnMouseMove;
        _pictureBox.MouseLeave += PictureBoxOnMouseLeave;
        _pictureBox.Paint += PictureBoxOnPaint;

        ResumeLayout(false);
    }

    /// <summary>
    /// Occurs whenever the mouse moves inside the monitor preview.
    /// </summary>
    public event EventHandler<Point>? MouseMovedInMonitorSpace;

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
        public Rectangle MonRel { get; set; }

        public Color Color { get; set; }

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
    public void Bind(MonitorInfo monitor, TimeSpan? cadence = null)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        Unbind();
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
        var started = host.Start(preferGpu: false);
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

    /// <summary>
    /// Stops the preview and releases any held resources.
    /// </summary>
    public void Unbind()
    {
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
            host.Stop();
        }
        catch
        {
            // Ignore preview stop failures.
        }

        host.Dispose();

        _pictureBox.Image = null;
        SetSimulationRects(Array.Empty<SimRect>());
    }

    /// <summary>
    /// Temporarily suspends the active preview capture, if any.
    /// </summary>
    public void SuspendCapture()
    {
        _host?.SuspendCapture();
    }

    /// <summary>
    /// Resumes a previously suspended preview capture, if any.
    /// </summary>
    public void ResumeCapture()
    {
        _host?.ResumeCapture();
    }

    /// <summary>
    /// Pauses frame processing while keeping the current frame visible.
    /// </summary>
    public void Pause()
    {
        _host?.Pause();
    }

    /// <summary>
    /// Resumes frame processing after a pause.
    /// </summary>
    public void Resume()
    {
        _host?.Resume();
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

    private void PictureBoxOnMouseMove(object? sender, MouseEventArgs e)
    {
        UpdateGlyphTooltip(e.Location);

        var monitor = _monitor;
        var image = _pictureBox.Image;

        if (monitor is null || image is null)
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

        MouseMovedInMonitorSpace?.Invoke(this, new Point(monitorX, monitorY));
    }

    private void PictureBoxOnMouseLeave(object? sender, EventArgs e)
    {
        ClearGlyphTooltip();
        MonitorMouseLeft?.Invoke(this, EventArgs.Empty);
    }

    private void PictureBoxOnPaint(object? sender, PaintEventArgs e)
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
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        _glyphRegions.Clear();

        foreach (var rect in _simRects)
        {
            if (rect.MonRel.Width <= 0 || rect.MonRel.Height <= 0)
            {
                continue;
            }

            var canvasRect = new RectangleF(
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
                baseColor = Color.DodgerBlue;
            }

            using (var fill = new SolidBrush(Color.FromArgb(96, baseColor)))
            {
                graphics.FillRectangle(fill, canvasRect);
            }

            var borderColor = Color.FromArgb(220, baseColor);
            using (var border = new Pen(borderColor, 2f))
            {
                graphics.DrawRectangle(border, canvasRect.X, canvasRect.Y, canvasRect.Width, canvasRect.Height);
            }

            DrawLabel(graphics, rect, canvasRect);
            DrawIndicatorGlyphs(graphics, rect, canvasRect, borderColor);
        }
    }

    private void DrawPlaceholder(Graphics graphics, string message)
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
        var layout = Rectangle.Inflate(bounds, -marginX, -marginY);
        if (layout.Width <= 0 || layout.Height <= 0)
        {
            layout = bounds;
        }

        using var background = new SolidBrush(Color.FromArgb(210, Color.White));
        using var border = new Pen(Color.FromArgb(160, 0, 0, 0), 1.5f);
        graphics.FillRectangle(background, layout);
        graphics.DrawRectangle(border, layout);

        var font = Font ?? Control.DefaultFont;
        TextRenderer.DrawText(
            graphics,
            text,
            font,
            layout,
            Color.Black,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
    }

    private void DrawLabel(Graphics graphics, SimRect rect, RectangleF canvasRect)
    {
        var label = FormatLabel(rect);
        if (string.IsNullOrWhiteSpace(label))
        {
            return;
        }

        var labelSize = TextRenderer.MeasureText(label, Font, Size.Empty, TextFormatFlags.NoPadding);
        var labelRect = new Rectangle(
            (int)Math.Round(canvasRect.X + 4f),
            (int)Math.Round(canvasRect.Y + 4f),
            Math.Max(labelSize.Width + 8, 0),
            Math.Max(labelSize.Height + 4, 0));

        if (labelRect.Width <= 0 || labelRect.Height <= 0)
        {
            return;
        }

        using var labelBrush = new SolidBrush(Color.FromArgb(180, 0, 0, 0));
        graphics.FillRectangle(labelBrush, labelRect);
        TextRenderer.DrawText(
            graphics,
            label,
            Font,
            labelRect,
            Color.White,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private void DrawIndicatorGlyphs(Graphics graphics, SimRect rect, RectangleF canvasRect, Color borderColor)
    {
        var rightEdge = canvasRect.Right - 4f;

        void DrawGlyph(string glyph, string tooltip)
        {
            if (string.IsNullOrEmpty(glyph))
            {
                return;
            }

            var glyphSize = TextRenderer.MeasureText(glyph, Font, Size.Empty, TextFormatFlags.NoPadding);
            var glyphRect = new Rectangle(
                (int)Math.Round(rightEdge - glyphSize.Width - 6f),
                (int)Math.Round(canvasRect.Y + 4f),
                glyphSize.Width + 6,
                glyphSize.Height + 6);

            if (glyphRect.Width <= 0 || glyphRect.Height <= 0)
            {
                return;
            }

            using var glyphFill = new SolidBrush(Color.FromArgb(235, 255, 255, 255));
            graphics.FillEllipse(glyphFill, glyphRect);

            using var glyphBorder = new Pen(borderColor, 1.5f);
            graphics.DrawEllipse(glyphBorder, glyphRect);

            TextRenderer.DrawText(
                graphics,
                glyph,
                Font,
                glyphRect,
                Color.Black,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            _glyphRegions.Add((new RectangleF(glyphRect.X, glyphRect.Y, glyphRect.Width, glyphRect.Height), tooltip));
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

    private static RectangleF GetImageDisplayRectangle(PictureBox pictureBox)
    {
        Image? image;
        try
        {
            image = pictureBox.Image;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            Logger.Debug(ex, "Falha ao obter imagem atual do preview.");
            return RectangleF.Empty;
        }

        if (image is null)
        {
            return RectangleF.Empty;
        }

        if (!TryGetImageSize(image, out var imageWidth, out var imageHeight))
        {
            return RectangleF.Empty;
        }

        return pictureBox.SizeMode switch
        {
            PictureBoxSizeMode.Normal or PictureBoxSizeMode.AutoSize => new RectangleF(0, 0, imageWidth, imageHeight),
            PictureBoxSizeMode.StretchImage => new RectangleF(0, 0, pictureBox.ClientSize.Width, pictureBox.ClientSize.Height),
            PictureBoxSizeMode.CenterImage => new RectangleF(
                (pictureBox.ClientSize.Width - imageWidth) / 2f,
                (pictureBox.ClientSize.Height - imageHeight) / 2f,
                imageWidth,
                imageHeight),
            PictureBoxSizeMode.Zoom => CalculateZoomRectangle(pictureBox, image, imageWidth, imageHeight),
            _ => new RectangleF(0, 0, pictureBox.ClientSize.Width, pictureBox.ClientSize.Height),
        };
    }

    private static RectangleF CalculateZoomRectangle(PictureBox pictureBox, Image image, int imageWidth, int imageHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0)
        {
            return RectangleF.Empty;
        }

        var clientSize = pictureBox.ClientSize;
        if (clientSize.Width <= 0 || clientSize.Height <= 0)
        {
            return RectangleF.Empty;
        }

        var ratio = Math.Min((float)clientSize.Width / imageWidth, (float)clientSize.Height / imageHeight);
        var width = imageWidth * ratio;
        var height = imageHeight * ratio;
        var x = (clientSize.Width - width) / 2f;
        var y = (clientSize.Height - height) / 2f;
        return new RectangleF(x, y, width, height);
    }

    private static bool TryGetImageSize(Image image, out int width, out int height)
    {
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

    private void UpdateGlyphTooltip(Point location)
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
