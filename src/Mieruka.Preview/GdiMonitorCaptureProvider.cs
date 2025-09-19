using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Mieruka.Core.Models;

namespace Mieruka.Preview;

/// <summary>
/// Fallback monitor capture implementation using GDI APIs.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GdiMonitorCaptureProvider : IMonitorCapture
{
    private const int TargetFramesPerSecond = 30;

    private CancellationTokenSource? _cts;
    private Task? _captureLoop;
    private MonitorUtilities.RECT _monitorBounds;
    private string _deviceName = string.Empty;

    /// <inheritdoc />
    public event EventHandler<MonitorFrameArrivedEventArgs>? FrameArrived;

    /// <inheritdoc />
    public bool IsSupported => OperatingSystem.IsWindows();

    /// <inheritdoc />
    public Task StartAsync(MonitorInfo monitor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        if (monitor.DeviceName is null)
        {
            throw new ArgumentException("Monitor device name is not defined.", nameof(monitor));
        }

        if (_captureLoop is { IsCompleted: false })
        {
            throw new InvalidOperationException("The capture session is already running.");
        }

        if (!MonitorUtilities.TryGetMonitorHandle(monitor.DeviceName, out _, out var bounds))
        {
            throw new InvalidOperationException($"Unable to locate monitor '{monitor.DeviceName}'.");
        }

        _monitorBounds = bounds;
        _deviceName = monitor.DeviceName;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _captureLoop = Task.Run(() => CaptureLoopAsync(_cts.Token), CancellationToken.None);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask StopAsync()
    {
        if (_cts is null)
        {
            return;
        }

        try
        {
            _cts.Cancel();
            if (_captureLoop is not null)
            {
                await _captureLoop.ConfigureAwait(false);
            }
        }
        finally
        {
            _captureLoop = null;
            _cts.Dispose();
            _cts = null;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private async Task CaptureLoopAsync(CancellationToken cancellationToken)
    {
        var frameInterval = TimeSpan.FromMilliseconds(1000.0 / TargetFramesPerSecond);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var bitmap = CaptureFrame();
                DispatchFrame(bitmap);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Swallow unexpected exceptions to keep the capture loop alive.
            }

            try
            {
                await Task.Delay(frameInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private Bitmap CaptureFrame()
    {
        if (string.IsNullOrWhiteSpace(_deviceName))
        {
            throw new InvalidOperationException("The capture provider has not been initialized.");
        }

        var width = _monitorBounds.Width;
        var height = _monitorBounds.Height;

        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("The monitor dimensions are invalid.");
        }

        var hdcSource = NativeMethods.CreateDC(_deviceName, _deviceName, null, IntPtr.Zero);
        if (hdcSource == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create monitor device context.");
        }

        try
        {
            var hdcMemory = NativeMethods.CreateCompatibleDC(hdcSource);
            if (hdcMemory == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create memory device context.");
            }

            try
            {
                var hBitmap = NativeMethods.CreateCompatibleBitmap(hdcSource, width, height);
                if (hBitmap == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Failed to allocate bitmap for capture.");
                }

                try
                {
                    var previous = NativeMethods.SelectObject(hdcMemory, hBitmap);

                    if (!NativeMethods.BitBlt(
                            hdcMemory,
                            0,
                            0,
                            width,
                            height,
                            hdcSource,
                            _monitorBounds.Left,
                            _monitorBounds.Top,
                            NativeMethods.TernaryRasterOperations.SRCCOPY))
                    {
                        throw new InvalidOperationException("The BitBlt operation failed.");
                    }

                    NativeMethods.SelectObject(hdcMemory, previous);

                    var bitmap = Image.FromHbitmap(hBitmap);
                    return (Bitmap)bitmap;
                }
                finally
                {
                    NativeMethods.DeleteObject(hBitmap);
                }
            }
            finally
            {
                NativeMethods.DeleteDC(hdcMemory);
            }
        }
        finally
        {
            NativeMethods.DeleteDC(hdcSource);
        }
    }

    private void DispatchFrame(Bitmap bitmap)
    {
        var handler = FrameArrived;
        if (handler is null)
        {
            bitmap.Dispose();
            return;
        }

        handler(this, new MonitorFrameArrivedEventArgs(bitmap, DateTimeOffset.UtcNow));
    }

    private static class NativeMethods
    {
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateDC(string lpszDriver, string? lpszDevice, string? lpszOutput, IntPtr lpInitData);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BitBlt(IntPtr hdc, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, TernaryRasterOperations dwRop);

        [Flags]
        public enum TernaryRasterOperations : uint
        {
            SRCCOPY = 0x00CC0020,
        }
    }
}
