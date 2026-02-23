#nullable enable
using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Mieruka.App.Forms.Controls;

/// <summary>
/// Button with rounded corners, hover lightening, and press darkening effects.
/// Drop-in replacement for <see cref="Button"/>.
/// </summary>
internal sealed class ModernButton : Button
{
    private bool _isHovered;
    private bool _isPressed;
    private Color _baseBackColor;
    private bool _baseBackColorInitialized;

    public ModernButton()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        DoubleBuffered = true;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
    }

    [DefaultValue(6)]
    public int CornerRadius { get; set; } = 6;

    [DefaultValue(20)]
    public int HoverLightenPercent { get; set; } = 20;

    [DefaultValue(15)]
    public int PressedDarkenPercent { get; set; } = 15;

    /// <summary>
    /// Swallow the Designer-generated <c>UseVisualStyleBackColor = true</c>.
    /// It is meaningless for custom-painted buttons and can interfere with BackColor resolution.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public new bool UseVisualStyleBackColor
    {
        get => false;
        set { /* intentionally ignored */ }
    }

    private Color ResolvedBaseColor
    {
        get
        {
            if (!_baseBackColorInitialized)
            {
                _baseBackColor = BackColor;
                _baseBackColorInitialized = true;
            }

            // Guard against transparent or empty colors (can happen when no explicit BackColor is set).
            if (_baseBackColor.A == 0 || _baseBackColor == Color.Empty)
            {
                return SystemColors.Control;
            }

            return _baseBackColor;
        }
    }

    public override Color BackColor
    {
        get => base.BackColor;
        set
        {
            base.BackColor = value;
            _baseBackColor = value;
            _baseBackColorInitialized = true;
            Invalidate();
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _isHovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _isHovered = false;
        _isPressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _isPressed = true;
            Invalidate();
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _isPressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        _isHovered = false;
        _isPressed = false;
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateClipRegion();
    }

    private void UpdateClipRegion()
    {
        var rect = ClientRectangle;
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        var drawRect = new Rectangle(rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
        var radius = Math.Min(CornerRadius, Math.Min(drawRect.Width, drawRect.Height) / 2);

        using var path = CreateRoundedRectPath(drawRect, radius);
        var oldRegion = Region;
        Region = new Region(path);
        oldRegion?.Dispose();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var rect = ClientRectangle;
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        // Clear entire background with parent color to avoid artifacts outside rounded corners.
        var parentColor = Parent?.BackColor ?? SystemColors.Control;
        using (var clearBrush = new SolidBrush(parentColor))
        {
            g.FillRectangle(clearBrush, rect);
        }

        // Shrink by 1 pixel so the border fits inside
        var drawRect = new Rectangle(rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
        var radius = Math.Min(CornerRadius, Math.Min(drawRect.Width, drawRect.Height) / 2);

        using var path = CreateRoundedRectPath(drawRect, radius);

        // Determine fill color
        var baseColor = ResolvedBaseColor;
        Color fillColor;

        if (!Enabled)
        {
            fillColor = Blend(baseColor, SystemColors.Control, 0.5f);
        }
        else if (_isPressed)
        {
            fillColor = DarkenColor(baseColor, PressedDarkenPercent);
        }
        else if (_isHovered)
        {
            fillColor = LightenColor(baseColor, HoverLightenPercent);
        }
        else
        {
            fillColor = baseColor;
        }

        // Fill background
        using (var brush = new SolidBrush(fillColor))
        {
            g.FillPath(brush, path);
        }

        // Draw border
        var borderColor = Enabled
            ? DarkenColor(fillColor, 30)
            : SystemColors.ControlDark;
        using (var pen = new Pen(borderColor, 1f))
        {
            g.DrawPath(pen, path);
        }

        // Draw focused border (keyboard navigation)
        if (Focused && ShowFocusCues)
        {
            using var focusPath = CreateRoundedRectPath(
                Rectangle.Inflate(drawRect, -2, -2),
                Math.Max(radius - 2, 0));
            using var focusPen = new Pen(SystemColors.Highlight, 1f) { DashStyle = DashStyle.Dot };
            g.DrawPath(focusPen, focusPath);
        }

        // Draw text
        var textColor = Enabled ? ForeColor : SystemColors.GrayText;
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;

        flags |= TextAlign switch
        {
            ContentAlignment.MiddleLeft or ContentAlignment.TopLeft or ContentAlignment.BottomLeft
                => TextFormatFlags.Left,
            ContentAlignment.MiddleRight or ContentAlignment.TopRight or ContentAlignment.BottomRight
                => TextFormatFlags.Right,
            _ => TextFormatFlags.HorizontalCenter,
        };

        var textRect = new Rectangle(
            drawRect.X + Padding.Left,
            drawRect.Y + Padding.Top,
            drawRect.Width - Padding.Horizontal,
            drawRect.Height - Padding.Vertical);

        TextRenderer.DrawText(g, Text, Font, textRect, textColor, flags);
    }

    private static GraphicsPath CreateRoundedRectPath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;

        if (radius <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }

        // Top-left
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        // Top-right
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        // Bottom-right
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        // Bottom-left
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }

    private static Color LightenColor(Color color, int percent)
    {
        var factor = percent / 100f;
        var r = (int)Math.Min(255, color.R + (255 - color.R) * factor);
        var g = (int)Math.Min(255, color.G + (255 - color.G) * factor);
        var b = (int)Math.Min(255, color.B + (255 - color.B) * factor);
        return Color.FromArgb(color.A, r, g, b);
    }

    private static Color DarkenColor(Color color, int percent)
    {
        var factor = 1f - percent / 100f;
        var r = (int)Math.Max(0, color.R * factor);
        var g = (int)Math.Max(0, color.G * factor);
        var b = (int)Math.Max(0, color.B * factor);
        return Color.FromArgb(color.A, r, g, b);
    }

    private static Color Blend(Color a, Color b, float ratio)
    {
        var inv = 1f - ratio;
        return Color.FromArgb(
            255,
            (int)(a.R * ratio + b.R * inv),
            (int)(a.G * ratio + b.G * inv),
            (int)(a.B * ratio + b.B * inv));
    }
}
