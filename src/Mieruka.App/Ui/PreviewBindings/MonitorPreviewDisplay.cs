using System;
using System.Drawing;
using System.Windows.Forms;
using Mieruka.Core.Models;

namespace Mieruka.App.Ui.PreviewBindings;

/// <summary>
/// Provides a reusable monitor preview control backed by <see cref="MonitorPreviewHost"/>.
/// </summary>
public sealed class MonitorPreviewDisplay : UserControl
{
    private readonly PictureBox _pictureBox;
    private MonitorPreviewHost? _host;
    private MonitorInfo? _monitor;

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

        _pictureBox.MouseMove += PictureBoxOnMouseMove;
        _pictureBox.MouseLeave += PictureBoxOnMouseLeave;

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
        var host = new MonitorPreviewHost(monitorId, _pictureBox);

        if (cadence is TimeSpan cadenceValue)
        {
            host.FrameThrottle = cadenceValue < TimeSpan.Zero ? TimeSpan.Zero : cadenceValue;
        }

        // The editor always prefers the BitBlt path to avoid GPU capture glitches in nested previews.
        host.Start(preferGpu: false);
        _host = host;
    }

    /// <summary>
    /// Stops the preview and releases any held resources.
    /// </summary>
    public void Unbind()
    {
        var host = _host;
        _host = null;
        _monitor = null;

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

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Unbind();
            _pictureBox.MouseMove -= PictureBoxOnMouseMove;
            _pictureBox.MouseLeave -= PictureBoxOnMouseLeave;
            _pictureBox.Dispose();
        }

        base.Dispose(disposing);
    }

    private void PictureBoxOnMouseMove(object? sender, MouseEventArgs e)
    {
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
        MonitorMouseLeft?.Invoke(this, EventArgs.Empty);
    }

    private static RectangleF GetImageDisplayRectangle(PictureBox pictureBox)
    {
        var image = pictureBox.Image;
        if (image is null)
        {
            return RectangleF.Empty;
        }

        return pictureBox.SizeMode switch
        {
            PictureBoxSizeMode.Normal or PictureBoxSizeMode.AutoSize => new RectangleF(0, 0, image.Width, image.Height),
            PictureBoxSizeMode.StretchImage => new RectangleF(0, 0, pictureBox.ClientSize.Width, pictureBox.ClientSize.Height),
            PictureBoxSizeMode.CenterImage => new RectangleF(
                (pictureBox.ClientSize.Width - image.Width) / 2f,
                (pictureBox.ClientSize.Height - image.Height) / 2f,
                image.Width,
                image.Height),
            PictureBoxSizeMode.Zoom => CalculateZoomRectangle(pictureBox, image),
            _ => new RectangleF(0, 0, pictureBox.ClientSize.Width, pictureBox.ClientSize.Height),
        };
    }

    private static RectangleF CalculateZoomRectangle(PictureBox pictureBox, Image image)
    {
        if (image.Width <= 0 || image.Height <= 0)
        {
            return RectangleF.Empty;
        }

        var clientSize = pictureBox.ClientSize;
        if (clientSize.Width <= 0 || clientSize.Height <= 0)
        {
            return RectangleF.Empty;
        }

        var ratio = Math.Min((float)clientSize.Width / image.Width, (float)clientSize.Height / image.Height);
        var width = image.Width * ratio;
        var height = image.Height * ratio;
        var x = (clientSize.Width - width) / 2f;
        var y = (clientSize.Height - height) / 2f;
        return new RectangleF(x, y, width, height);
    }
}
