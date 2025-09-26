#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Mieruka.Core.Models;
using Mieruka.Core.Monitors;
using Mieruka.Preview;

namespace Mieruka.App.Ui.PreviewBindings;

/// <summary>
/// Hosts a monitor preview session and binds frames to a <see cref="PictureBox"/>.
/// </summary>
public sealed class MonitorPreviewHost : IDisposable
{
    private static readonly Font OverlayFont = SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont;

    private readonly PictureBox _target;
    private readonly MonitorDescriptor _monitor;
    private readonly bool _preferGpu;
    private readonly object _gate = new();
    private Bitmap? _currentFrame;
    private bool _disposed;
    private Rectangle _monitorBounds;
    private Rectangle _monitorWorkArea;
    private MonitorOrientation _orientation;
    private int _rotation;
    private int _refreshRate;

    public MonitorPreviewHost(PictureBox target, MonitorDescriptor monitor, bool preferGpu)
    {
        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        if (monitor is null)
        {
            throw new ArgumentNullException(nameof(monitor));
        }

        _target = target;
        _monitor = monitor;
        _preferGpu = preferGpu;

        MonitorId = CreateMonitorId(monitor);
        _monitorBounds = monitor.Bounds;
        _monitorWorkArea = monitor.WorkArea;
        _orientation = monitor.Orientation;
        _rotation = monitor.Rotation;
        _refreshRate = monitor.RefreshHz;

        EnsurePictureBoxSizeMode();
    }

    public MonitorPreviewHost(PictureBox? target, MonitorDescriptor monitor)
        : this(target ?? throw new ArgumentNullException(nameof(target)), monitor, preferGpu: true)
    {
    }

    public MonitorPreviewHost(MonitorDescriptor monitor, PictureBox target)
        : this(target ?? throw new ArgumentNullException(nameof(target)), monitor, preferGpu: true)
    {
    }

    public MonitorPreviewHost(string monitorId, PictureBox target)
        : this(target ?? throw new ArgumentNullException(nameof(target)), CreateDescriptorFromMonitorId(monitorId ?? throw new ArgumentNullException(nameof(monitorId))), preferGpu: true)
    {
    }

    public static bool IsPreviewSupported()
        => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041);

    /// <summary>
    /// Gets the identifier of the monitor being previewed.
    /// </summary>
    public string MonitorId { get; }

    /// <summary>
    /// Gets the active capture session, if any.
    /// </summary>
    public IMonitorCapture? Capture { get; private set; }

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
    /// Starts the preview using GPU capture when available.
    /// </summary>
    public void Start()
    {
        if (_disposed || Capture is not null)
        {
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (!IsPreviewSupported())
        {
            return;
        }

        PopulateMetadataFromMonitor();

        foreach (var factory in EnumerateFactories(_preferGpu))
        {
            IMonitorCapture? capture = null;
            try
            {
                capture = factory();
                capture.FrameArrived += OnFrameArrived;

                lock (_gate)
                {
                    Capture = capture;
                }

                return;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Preview] Falha ao iniciar captura para {MonitorId}: {ex.Message}");
                if (capture is not null)
                {
                    capture.FrameArrived -= OnFrameArrived;
                    SafeDispose(capture);
                }
            }
        }
    }

    /// <summary>
    /// Stops the current preview session.
    /// </summary>
    public void Stop()
    {
        IMonitorCapture? capture;
        lock (_gate)
        {
            capture = Capture;
            Capture = null;
        }

        if (capture is null)
        {
            ClearFrame();
            return;
        }

        capture.FrameArrived -= OnFrameArrived;
        SafeDispose(capture);
        ClearFrame();
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

    private IEnumerable<Func<IMonitorCapture>> EnumerateFactories(bool preferGpu)
    {
        if (preferGpu)
        {
            yield return () => CreateForMonitor.Gpu(MonitorId);
            yield return () => CreateForMonitor.Gdi(MonitorId);
        }
        else
        {
            yield return () => CreateForMonitor.Gdi(MonitorId);
            yield return () => CreateForMonitor.Gpu(MonitorId);
        }
    }

    private void OnFrameArrived(object? sender, MonitorFrameArrivedEventArgs e)
    {
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

        ApplyFrame(clone);
    }

    private void ApplyFrame(Bitmap frame)
    {
        if (_target.IsDisposed)
        {
            frame.Dispose();
            return;
        }

        if (_target.InvokeRequired)
        {
            try
            {
                _target.BeginInvoke((Action)(() => ApplyFrame(frame)));
            }
            catch
            {
                frame.Dispose();
            }

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

    private static MonitorDescriptor CreateDescriptorFromMonitorId(string monitorId)
    {
        return new MonitorDescriptor
        {
            DeviceName = monitorId,
            FriendlyName = monitorId,
        };
    }

    private void EnsurePictureBoxSizeMode()
    {
        if (_target.SizeMode != PictureBoxSizeMode.Zoom)
        {
            _target.SizeMode = PictureBoxSizeMode.Zoom;
        }
    }

    private void PopulateMetadataFromMonitor()
    {
        if (_monitorBounds == Rectangle.Empty && _monitor.Bounds != Rectangle.Empty)
        {
            _monitorBounds = _monitor.Bounds;
        }

        if (_monitorWorkArea == Rectangle.Empty && _monitor.WorkArea != Rectangle.Empty)
        {
            _monitorWorkArea = _monitor.WorkArea;
        }

        if (_orientation == MonitorOrientation.Unknown && _monitor.Orientation != MonitorOrientation.Unknown)
        {
            _orientation = _monitor.Orientation;
        }

        if (_rotation == 0 && _monitor.Rotation != 0)
        {
            _rotation = _monitor.Rotation;
        }

        if (_refreshRate == 0 && _monitor.RefreshHz > 0)
        {
            _refreshRate = _monitor.RefreshHz;
        }

        if (_monitorBounds != Rectangle.Empty)
        {
            return;
        }

        try
        {
            var info = MonitorLocator.Find(MonitorId);
            if (info is null)
            {
                return;
            }

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
        }
        catch
        {
            // Metadata retrieval is best-effort only.
        }
    }

#if DEBUG
    private void DrawDebugOverlay(Bitmap bitmap)
    {
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
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

            var font = OverlayFont;
            var measured = graphics.MeasureString(text, font);
            var rect = new RectangleF(4, 4, measured.Width + 8, measured.Height + 4);
            graphics.FillRectangle(background, rect);
            graphics.DrawString(text, font, foreground, new PointF(rect.Left + 4, rect.Top + 2));
        }
        catch
        {
            // Debug overlay is best-effort only.
        }
    }
#endif
}
