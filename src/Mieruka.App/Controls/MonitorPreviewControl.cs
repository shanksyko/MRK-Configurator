using System;
using System.Drawing;
using System.Windows.Forms;
using Mieruka.App.Services;
using Mieruka.Core.Models;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Mieruka.App.Controls;

/// <summary>
/// Renders a monitor thumbnail and allows applying rectangular selections.
/// </summary>
internal sealed class MonitorPreviewControl : WinForms.UserControl
{
    private readonly WinForms.Label _titleLabel;
    private readonly MonitorCanvas _canvas;
    private bool _isSelected;

    /// <summary>
    /// Initializes a new instance of the <see cref="MonitorPreviewControl"/> class.
    /// </summary>
    public MonitorPreviewControl()
    {
        Padding = new WinForms.Padding(8);
        BackColor = SystemColors.ControlLightLight;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

        var captionFont = ResolveFont(SystemFonts.CaptionFont);

        _titleLabel = new WinForms.Label
        {
            Dock = WinForms.DockStyle.Top,
            TextAlign = ContentAlignment.MiddleCenter,
            Height = 28,
            Font = new Drawing.Font(captionFont, FontStyle.Bold),
        };

        _canvas = new MonitorCanvas
        {
            Dock = WinForms.DockStyle.Fill,
            Margin = new WinForms.Padding(4),
        };

        Controls.Add(_canvas);
        Controls.Add(_titleLabel);

        _canvas.SelectionApplied += CanvasOnSelectionApplied;
        _canvas.DragEnter += CanvasOnDragEnter;
        _canvas.DragOver += CanvasOnDragEnter;
        _canvas.DragLeave += CanvasOnDragLeave;
        _canvas.DragDrop += CanvasOnDragDrop;
        _canvas.MouseClick += CanvasOnMouseClick;
        _titleLabel.MouseClick += CanvasOnMouseClick;
        MouseClick += CanvasOnMouseClick;
    }

    /// <summary>
    /// Occurs when an entry is dropped onto the monitor.
    /// </summary>
    public event EventHandler<EntryDroppedEventArgs>? EntryDropped;

    /// <summary>
    /// Occurs when the user finalizes a rectangular selection.
    /// </summary>
    public event EventHandler<SelectionAppliedEventArgs>? SelectionApplied;

    /// <summary>
    /// Occurs when the monitor preview is clicked.
    /// </summary>
    public event EventHandler? MonitorSelected;

    private static Drawing.Font ResolveFont(Drawing.Font? prototype)
    {
        if (prototype is not null)
        {
            return prototype;
        }

        if (SystemFonts.DefaultFont is { } defaultFont)
        {
            return defaultFont;
        }

        return WinForms.Control.DefaultFont;
    }

    /// <summary>
    /// Gets or sets the monitor displayed by the control.
    /// </summary>
    public MonitorInfo? Monitor
    {
        get => _canvas.Monitor;
        set
        {
            _canvas.Monitor = value;
            if (value is null)
            {
                _titleLabel.Text = "Sem monitor";
            }
            else
            {
                var name = string.IsNullOrWhiteSpace(value.Name)
                    ? $"Monitor {value.Key.DisplayIndex + 1}"
                    : value.Name;
                var scaleText = value.Scale > 0 ? $"{value.Scale:P0}" : "1";
                _titleLabel.Text = $"{name} ({value.Width}x{value.Height} @ {scaleText})";
            }
        }
    }

    /// <summary>
    /// Updates the control to display the provided window configuration.
    /// </summary>
    /// <param name="window">Window configuration to render.</param>
    public void DisplayWindow(WindowConfig? window)
    {
        _canvas.DisplayWindow(window);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the monitor preview is selected.
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            Invalidate();
        }
    }

    private void CanvasOnDragEnter(object? sender, DragEventArgs e)
    {
        if (!EntryReference.TryGet(e.Data, out _))
        {
            e.Effect = DragDropEffects.None;
            return;
        }

        e.Effect = DragDropEffects.Move;
        _canvas.ShowDropCue(true);
    }

    private void CanvasOnDragLeave(object? sender, EventArgs e)
    {
        _canvas.ShowDropCue(false);
    }

    private void CanvasOnDragDrop(object? sender, DragEventArgs e)
    {
        _canvas.ShowDropCue(false);

        if (Monitor is null)
        {
            e.Effect = DragDropEffects.None;
            return;
        }

        if (!EntryReference.TryGet(e.Data, out var entry) || entry is null)
        {
            e.Effect = DragDropEffects.None;
            return;
        }

        EntryDropped?.Invoke(this, new EntryDroppedEventArgs(entry, Monitor));
        e.Effect = DragDropEffects.Move;
    }

    private void CanvasOnSelectionApplied(object? sender, Drawing.Rectangle selection)
    {
        if (Monitor is null)
        {
            return;
        }

        SelectionApplied?.Invoke(this, new SelectionAppliedEventArgs(Monitor, selection));
    }

    private void CanvasOnMouseClick(object? sender, WinForms.MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            MonitorSelected?.Invoke(this, EventArgs.Empty);
        }
    }

    // Cached pens for OnPaint to avoid per-paint GDI allocations.
    private static readonly Drawing.Pen s_selectedPen = new(Drawing.Color.DodgerBlue, 2);
    private static readonly Drawing.Pen s_normalPen = new(SystemColors.ControlDark, 1);

    protected override void OnPaint(WinForms.PaintEventArgs e)
    {
        base.OnPaint(e);

        var pen = _isSelected ? s_selectedPen : s_normalPen;
        var rect = ClientRectangle;
        rect.Width -= 1;
        rect.Height -= 1;

        e.Graphics.DrawRectangle(pen, rect);
    }

    /// <summary>
    /// Event payload produced when an entry is dropped onto the control.
    /// </summary>
    public sealed class EntryDroppedEventArgs : EventArgs
    {
        internal EntryDroppedEventArgs(EntryReference entry, MonitorInfo monitor)
        {
            Entry = entry;
            Monitor = monitor;
        }

        /// <summary>
        /// Gets the dropped entry.
        /// </summary>
        public EntryReference Entry { get; }

        /// <summary>
        /// Gets the monitor associated with the drop location.
        /// </summary>
        public MonitorInfo Monitor { get; }
    }

    /// <summary>
    /// Event payload produced when a selection is applied.
    /// </summary>
    public sealed class SelectionAppliedEventArgs : EventArgs
    {
        internal SelectionAppliedEventArgs(MonitorInfo monitor, Drawing.Rectangle selection)
        {
            Monitor = monitor;
            Selection = selection;
        }

        /// <summary>
        /// Gets the monitor that received the selection.
        /// </summary>
        public MonitorInfo Monitor { get; }

        /// <summary>
        /// Gets the bounds of the selection in monitor coordinates.
        /// </summary>
        public Drawing.Rectangle Selection { get; }
    }

    private sealed class MonitorCanvas : WinForms.Panel
    {
        private readonly StringFormat _stringFormat = new()
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };

        // Cached GDI resources to avoid per-paint allocations (reduces GC pressure + stutter).
        private static readonly SolidBrush s_grayTextBrush = new(SystemColors.GrayText);
        private static readonly SolidBrush s_monitorFillBrush = new(Drawing.Color.FromArgb(234, 240, 246));
        private static readonly SolidBrush s_selectionOverlay = new(Drawing.Color.FromArgb(80, Drawing.Color.DeepSkyBlue));
        private static readonly Drawing.Pen s_monitorPen = new(Drawing.Color.SteelBlue, 2f);
        private static readonly Drawing.Pen s_monitorPenDropCue = new(Drawing.Color.SteelBlue, 4f);
        private static readonly Drawing.Pen s_selectionOutline = new(Drawing.Color.DeepSkyBlue, 2f);

        private Drawing.Rectangle? _selection;
        private Drawing.RectangleF _currentDrag;
        private bool _isSelecting;
        private Drawing.PointF _dragStart;
        private bool _showDropCue;

        public MonitorCanvas()
        {
            DoubleBuffered = true;
            AllowDrop = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Drawing.Color.FromArgb(248, 248, 248);
        }

        public MonitorInfo? Monitor { get; set; }

        public event EventHandler<Drawing.Rectangle>? SelectionApplied;

        public void DisplayWindow(WindowConfig? window)
        {
            if (Monitor is null)
            {
                _selection = null;
                Invalidate();
                return;
            }

            if (window is null)
            {
                _selection = null;
                Invalidate();
                return;
            }

            if (!KeysEqual(window.Monitor, Monitor.Key))
            {
                _selection = null;
                Invalidate();
                return;
            }

            var monitorBounds = new Drawing.Rectangle(0, 0, Monitor.Width, Monitor.Height);

            Drawing.Rectangle selection;
            if (window.FullScreen)
            {
                selection = monitorBounds;
            }
            else
            {
                var x = window.X ?? 0;
                var y = window.Y ?? 0;
                var width = window.Width ?? monitorBounds.Width;
                var height = window.Height ?? monitorBounds.Height;
                selection = Drawing.Rectangle.Intersect(monitorBounds, new Drawing.Rectangle(x, y, width, height));
                if (selection.Width <= 0 || selection.Height <= 0)
                {
                    selection = monitorBounds;
                }
            }

            _selection = selection;
            Invalidate();
        }

        public void ShowDropCue(bool value)
        {
            if (_showDropCue == value)
            {
                return;
            }

            _showDropCue = value;
            Invalidate();
        }

        protected override void OnPaint(WinForms.PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            var displayFont = ResolveFont(Font);

            if (Monitor is null)
            {
                e.Graphics.DrawString("Nenhum monitor disponível", displayFont, s_grayTextBrush, ClientRectangle, _stringFormat);
                return;
            }

            if (!TryGetMonitorSurface(out var surface, out var scale))
            {
                e.Graphics.DrawString("Monitor inválido", displayFont, s_grayTextBrush, ClientRectangle, _stringFormat);
                return;
            }

            var monitorPen = _showDropCue ? s_monitorPenDropCue : s_monitorPen;

            e.Graphics.FillRectangle(s_monitorFillBrush, surface);
            e.Graphics.DrawRectangle(monitorPen, surface.X, surface.Y, surface.Width, surface.Height);

            Drawing.RectangleF selectionRect;

            if (_isSelecting)
            {
                selectionRect = Normalize(_currentDrag);
            }
            else if (_selection is { } selection)
            {
                selectionRect = new Drawing.RectangleF(
                    surface.Left + (selection.X * scale),
                    surface.Top + (selection.Y * scale),
                    selection.Width * scale,
                    selection.Height * scale);
            }
            else
            {
                selectionRect = Drawing.RectangleF.Empty;
            }

            if (selectionRect.Width > 0f && selectionRect.Height > 0f)
            {
                e.Graphics.FillRectangle(s_selectionOverlay, selectionRect);
                e.Graphics.DrawRectangle(s_selectionOutline, selectionRect.X, selectionRect.Y, selectionRect.Width, selectionRect.Height);
            }

            var caption = Monitor.DeviceName;
            if (!string.IsNullOrWhiteSpace(caption))
            {
                var captionBounds = new Drawing.RectangleF(surface.Left, surface.Bottom + 6f, surface.Width, displayFont.Height + 6f);
                var messageFont = ResolveFont(SystemFonts.MessageBoxFont ?? Font);
                e.Graphics.DrawString(caption, messageFont, s_grayTextBrush, captionBounds, _stringFormat);
            }
        }

        protected override void OnMouseDown(WinForms.MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            if (!TryGetMonitorSurface(out var surface, out _))
            {
                return;
            }

            Focus();
            Capture = true;
            _isSelecting = true;
            _currentDrag = Drawing.RectangleF.Empty;
            _dragStart = ClampToSurface(e.Location, surface);
        }

        protected override void OnMouseMove(WinForms.MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (!_isSelecting)
            {
                return;
            }

            if (!TryGetMonitorSurface(out var surface, out _))
            {
                return;
            }

            var current = ClampToSurface(e.Location, surface);
            _currentDrag = Drawing.RectangleF.FromLTRB(_dragStart.X, _dragStart.Y, current.X, current.Y);
            Invalidate();
        }

        protected override void OnMouseUp(WinForms.MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (e.Button != MouseButtons.Left || !_isSelecting)
            {
                return;
            }

            _isSelecting = false;
            Capture = false;

            if (Monitor is null)
            {
                return;
            }

            if (!TryGetMonitorSurface(out var surface, out var scale))
            {
                return;
            }

            var endPoint = ClampToSurface(e.Location, surface);
            var dragRect = Normalize(Drawing.RectangleF.FromLTRB(_dragStart.X, _dragStart.Y, endPoint.X, endPoint.Y));

            if (dragRect.Width < 4f || dragRect.Height < 4f)
            {
                dragRect = surface;
            }

            var selection = new Drawing.Rectangle(
                x: (int)Math.Round((dragRect.Left - surface.Left) / scale),
                y: (int)Math.Round((dragRect.Top - surface.Top) / scale),
                width: (int)Math.Round(dragRect.Width / scale),
                height: (int)Math.Round(dragRect.Height / scale));

            selection.Width = Math.Max(1, Math.Min(Monitor.Width - selection.X, selection.Width));
            selection.Height = Math.Max(1, Math.Min(Monitor.Height - selection.Y, selection.Height));

            _selection = selection;
            _currentDrag = Drawing.RectangleF.Empty;
            Invalidate();

            SelectionApplied?.Invoke(this, selection);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _stringFormat.Dispose();
            }

            base.Dispose(disposing);
        }

        private bool TryGetMonitorSurface(out Drawing.RectangleF surface, out float scale)
        {
            surface = Drawing.RectangleF.Empty;
            scale = 1f;

            if (Monitor is null || Monitor.Width <= 0 || Monitor.Height <= 0)
            {
                return false;
            }

            var padding = 12f;
            var availableWidth = Math.Max(1f, ClientSize.Width - (padding * 2));
            var availableHeight = Math.Max(1f, ClientSize.Height - (padding * 2));

            scale = (float)Math.Min(availableWidth / Monitor.Width, availableHeight / Monitor.Height);
            scale = Math.Max(scale, 0.01f);

            var width = Monitor.Width * scale;
            var height = Monitor.Height * scale;
            var left = (ClientSize.Width - width) / 2f;
            var top = (ClientSize.Height - height) / 2f;

            surface = new Drawing.RectangleF(left, top, width, height);
            return true;
        }

        private static Drawing.RectangleF Normalize(Drawing.RectangleF rectangle)
        {
            if (rectangle.Width >= 0f && rectangle.Height >= 0f)
            {
                return rectangle;
            }

            var left = Math.Min(rectangle.Left, rectangle.Right);
            var top = Math.Min(rectangle.Top, rectangle.Bottom);
            var right = Math.Max(rectangle.Left, rectangle.Right);
            var bottom = Math.Max(rectangle.Top, rectangle.Bottom);
            return Drawing.RectangleF.FromLTRB(left, top, right, bottom);
        }

        private static Drawing.PointF ClampToSurface(Drawing.Point point, Drawing.RectangleF surface)
        {
            var x = Math.Max(surface.Left, Math.Min(surface.Right, point.X));
            var y = Math.Max(surface.Top, Math.Min(surface.Bottom, point.Y));
            return new Drawing.PointF(x, y);
        }

        private static bool KeysEqual(MonitorKey left, MonitorKey right)
        {
            return string.Equals(left.DeviceId, right.DeviceId, StringComparison.OrdinalIgnoreCase)
                && left.DisplayIndex == right.DisplayIndex
                && left.AdapterLuidHigh == right.AdapterLuidHigh
                && left.AdapterLuidLow == right.AdapterLuidLow
                && left.TargetId == right.TargetId;
        }
    }
}
