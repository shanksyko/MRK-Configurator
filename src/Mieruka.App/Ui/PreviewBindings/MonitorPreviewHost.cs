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
    private readonly PictureBox _target;
    private readonly object _gate = new();
    private Bitmap? _currentFrame;
    private bool _disposed;

    public MonitorPreviewHost(string monitorId, PictureBox target)
    {
        MonitorId = monitorId ?? throw new ArgumentNullException(nameof(monitorId));
        _target = target ?? throw new ArgumentNullException(nameof(target));
    }

    public MonitorPreviewHost(MonitorDescriptor descriptor, PictureBox target)
        : this(CreateMonitorId(descriptor), target)
    {
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
    /// Starts the preview using GPU capture when available.
    /// </summary>
    public void Start(bool preferGpu)
    {
        if (_disposed || Capture is not null)
        {
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        foreach (var factory in EnumerateFactories(preferGpu))
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

        UpdateTarget(clone);
    }

    private void UpdateTarget(Bitmap frame)
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
                _target.BeginInvoke(new Action<Bitmap>(UpdateTarget), frame);
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
}
