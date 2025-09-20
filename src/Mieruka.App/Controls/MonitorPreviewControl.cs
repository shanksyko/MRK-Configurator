using System;
using System.Drawing;
using System.Windows.Forms;
using Mieruka.Core.Models;

namespace Mieruka.App.Controls;

/// <summary>
/// Renders a monitor thumbnail and allows applying rectangular selections.
/// </summary>
internal sealed class MonitorPreviewControl : UserControl
{
    private readonly Label _titleLabel;
    private readonly MonitorCanvas _canvas;

    /// <summary>
    /// Initializes a new instance of the <see cref="MonitorPreviewControl"/> class.
    /// </summary>
    public MonitorPreviewControl()
    {
        Padding = new Padding(8);
        BackColor = SystemColors.ControlLightLight;

        var captionFont = SystemFonts.CaptionFont ?? Control.DefaultFont;

        _titleLabel = new Label
        {
            Dock = DockStyle.Top,
            TextAlign = ContentAlignment.MiddleCenter,
            Height = 28,
            Font = new Font(captionFont, FontStyle.Bold),
        };

        _canvas = new MonitorCanvas
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(4),
        };

        Controls.Add(_canvas);
        Controls.Add(_titleLabel);

        _canvas.SelectionApplied += CanvasOnSelectionApplied;
        _canvas.DragEnter += CanvasOnDragEnter;
        _canvas.DragOver += CanvasOnDragEnter;
        _canvas.DragLeave += CanvasOnDragLeave;
        _canvas.DragDrop += CanvasOnDragDrop;
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
                _titleLabel.Text = $"{name} ({value.Width}x{value.Height})";
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

    private void CanvasOnSelectionApplied(object? sender, Rectangle selection)
    {
        if (Monitor is null)
        {
            return;
        }

        SelectionApplied?.Invoke(this, new SelectionAppliedEventArgs(Monitor, selection));
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
        internal SelectionAppliedEventArgs(MonitorInfo monitor, Rectangle selection)
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
        public Rectangle Selection { get; }
    }

    private sealed class MonitorCanvas : Panel
    {
        private readonly StringFormat _stringFormat = new()
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };

        private Rectangle? _selection;
        private RectangleF _currentDrag;
        private bool _isSelecting;
        private PointF _dragStart;
        private bool _showDropCue;

        public MonitorCanvas()
        {
            DoubleBuffered = true;
            AllowDrop = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Color.FromArgb(248, 248, 248);
        }

        public MonitorInfo? Monitor { get; set; }

        public event EventHandler<Rectangle>? SelectionApplied;

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

            var monitorBounds = new Rectangle(0, 0, Monitor.Width, Monitor.Height);

            Rectangle selection;
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
                selection = Rectangle.Intersect(monitorBounds, new Rectangle(x, y, width, height));
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

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            if (Monitor is null)
            {
                using var textBrush = new SolidBrush(SystemColors.GrayText);
                e.Graphics.DrawString("Nenhum monitor disponível", Font, textBrush, ClientRectangle, _stringFormat);
                return;
            }

            if (!TryGetMonitorSurface(out var surface, out var scale))
            {
                using var textBrush = new SolidBrush(SystemColors.GrayText);
                e.Graphics.DrawString("Monitor inválido", Font, textBrush, ClientRectangle, _stringFormat);
                return;
            }

            using var monitorBrush = new SolidBrush(Color.FromArgb(234, 240, 246));
            using var monitorPen = new Pen(Color.SteelBlue, _showDropCue ? 4f : 2f);

            e.Graphics.FillRectangle(monitorBrush, surface);
            e.Graphics.DrawRectangle(monitorPen, surface.X, surface.Y, surface.Width, surface.Height);

            RectangleF selectionRect;

            if (_isSelecting)
            {
                selectionRect = Normalize(_currentDrag);
            }
            else if (_selection is { } selection)
            {
                selectionRect = new RectangleF(
                    surface.Left + (selection.X * scale),
                    surface.Top + (selection.Y * scale),
                    selection.Width * scale,
                    selection.Height * scale);
            }
            else
            {
                selectionRect = RectangleF.Empty;
            }

            if (selectionRect.Width > 0f && selectionRect.Height > 0f)
            {
                using var overlay = new SolidBrush(Color.FromArgb(80, Color.DeepSkyBlue));
                using var outline = new Pen(Color.DeepSkyBlue, 2f);
                e.Graphics.FillRectangle(overlay, selectionRect);
                e.Graphics.DrawRectangle(outline, selectionRect.X, selectionRect.Y, selectionRect.Width, selectionRect.Height);
            }

            using var captionBrush = new SolidBrush(SystemColors.GrayText);
            var caption = Monitor.DeviceName;
            if (!string.IsNullOrWhiteSpace(caption))
            {
                var captionBounds = new RectangleF(surface.Left, surface.Bottom + 6f, surface.Width, Font.Height + 6f);
                var messageFont = SystemFonts.MessageBoxFont ?? Font ?? Control.DefaultFont;
                e.Graphics.DrawString(caption, messageFont, captionBrush, captionBounds, _stringFormat);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
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
            _currentDrag = RectangleF.Empty;
            _dragStart = ClampToSurface(e.Location, surface);
        }

        protected override void OnMouseMove(MouseEventArgs e)
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
            _currentDrag = RectangleF.FromLTRB(_dragStart.X, _dragStart.Y, current.X, current.Y);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
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
            var dragRect = Normalize(RectangleF.FromLTRB(_dragStart.X, _dragStart.Y, endPoint.X, endPoint.Y));

            if (dragRect.Width < 4f || dragRect.Height < 4f)
            {
                dragRect = surface;
            }

            var selection = new Rectangle(
                x: (int)Math.Round((dragRect.Left - surface.Left) / scale),
                y: (int)Math.Round((dragRect.Top - surface.Top) / scale),
                width: (int)Math.Round(dragRect.Width / scale),
                height: (int)Math.Round(dragRect.Height / scale));

            selection.Width = Math.Max(1, Math.Min(Monitor.Width - selection.X, selection.Width));
            selection.Height = Math.Max(1, Math.Min(Monitor.Height - selection.Y, selection.Height));

            _selection = selection;
            _currentDrag = RectangleF.Empty;
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

        private bool TryGetMonitorSurface(out RectangleF surface, out float scale)
        {
            surface = RectangleF.Empty;
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

            surface = new RectangleF(left, top, width, height);
            return true;
        }

        private static RectangleF Normalize(RectangleF rectangle)
        {
            if (rectangle.Width >= 0f && rectangle.Height >= 0f)
            {
                return rectangle;
            }

            var left = Math.Min(rectangle.Left, rectangle.Right);
            var top = Math.Min(rectangle.Top, rectangle.Bottom);
            var right = Math.Max(rectangle.Left, rectangle.Right);
            var bottom = Math.Max(rectangle.Top, rectangle.Bottom);
            return RectangleF.FromLTRB(left, top, right, bottom);
        }

        private static PointF ClampToSurface(Point point, RectangleF surface)
        {
            var x = Math.Max(surface.Left, Math.Min(surface.Right, point.X));
            var y = Math.Max(surface.Top, Math.Min(surface.Bottom, point.Y));
            return new PointF(x, y);
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
