using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Mieruka.Core.Models;
#if WINDOWS10_0_17763_0_OR_GREATER
using Mieruka.Preview.Capture.Interop;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
#endif

namespace Mieruka.Preview;

/// <summary>
/// Monitor capture provider that uses Windows.Graphics.Capture APIs.
/// </summary>
[SupportedOSPlatform("windows10.0.17763")]
public sealed class GraphicsCaptureProvider : IMonitorCapture
{
#if WINDOWS10_0_17763_0_OR_GREATER
    private static readonly ConcurrentDictionary<string, DateTime> _gpuBackoffUntil = new();
    private static readonly TimeSpan Backoff = TimeSpan.FromSeconds(60);
    private static int _gpuGloballyDisabled;

    private const int FramePoolBufferCount = 4;

    private readonly object _gate = new();
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private GraphicsCaptureItem? _captureItem;
    private IDirect3DDevice? _direct3DDevice;
    private ID3D11Device? _d3dDevice;
    private ID3D11DeviceContext? _d3dContext;
    private Windows.Graphics.SizeInt32 _currentSize;

    /// <inheritdoc />
    public event EventHandler<MonitorFrameArrivedEventArgs>? FrameArrived;

    /// <inheritdoc />
    [SupportedOSPlatform("windows10.0.17763")]
    public bool IsSupported => IsGraphicsCaptureAvailable;

    /// <inheritdoc />
    [SupportedOSPlatform("windows10.0.17763")]
    public Task StartAsync(MonitorInfo monitor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        cancellationToken.ThrowIfCancellationRequested();

        if (IsGpuGloballyDisabled)
        {
            throw new GraphicsCaptureUnavailableException("Windows Graphics Capture foi desativado para esta sessão.", isPermanent: true);
        }

        if (!IsGraphicsCaptureAvailable)
        {
            throw new GraphicsCaptureUnavailableException("Windows Graphics Capture não é suportado neste sistema.", isPermanent: true);
        }

        if (monitor.DeviceName is null)
        {
            throw new ArgumentException("Monitor device name is not defined.", nameof(monitor));
        }

        if (IsGpuInBackoff(monitor.Id))
        {
            throw new NotSupportedException("GPU capture em backoff para este display; use GDI.");
        }

        lock (_gate)
        {
            if (_session is not null)
            {
                throw new InvalidOperationException("Capture session already started.");
            }

            if (!MonitorUtilities.TryGetMonitorHandle(monitor.DeviceName, out var monitorHandle, out var bounds))
            {
                throw new InvalidOperationException($"Unable to locate monitor '{monitor.DeviceName}'.");
            }

            try
            {
                InitializeDirect3D();

                _captureItem = GraphicsCaptureInterop.CreateItemForMonitor(monitorHandle);
                _currentSize = _captureItem.Size;

                _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                    _direct3DDevice!,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    FramePoolBufferCount,
                    _captureItem.Size);

                _framePool.FrameArrived += OnFrameArrived;

                _session = _framePool.CreateCaptureSession(_captureItem);
                if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
                {
                    EnableCursorCapture(_session);
                }
                _session.StartCapture();

                // ensure bounds stored so fallback can use if needed
                _ = bounds;
            }
            catch
            {
                if (_framePool is not null)
                {
                    _framePool.FrameArrived -= OnFrameArrived;
                    _framePool.Dispose();
                    _framePool = null;
                }

                if (_session is not null)
                {
                    _session.Dispose();
                    _session = null;
                }

                _captureItem = null;
                ReleaseDirect3D();
                throw;
            }
        }

        return Task.CompletedTask;
    }

    public static bool MarkGpuBackoff(string monitorId, TimeSpan? durationOverride = null)
    {
        if (string.IsNullOrWhiteSpace(monitorId))
        {
            return false;
        }

        DateTime expiration;
        if (durationOverride == Timeout.InfiniteTimeSpan)
        {
            expiration = DateTime.MaxValue;
        }
        else
        {
            var backoff = durationOverride ?? Backoff;
            if (backoff <= TimeSpan.Zero)
            {
                expiration = DateTime.UtcNow;
            }
            else
            {
                expiration = DateTime.UtcNow + backoff;
            }
        }

        while (true)
        {
            if (_gpuBackoffUntil.TryGetValue(monitorId, out var current))
            {
                if (current >= expiration)
                {
                    return false;
                }

                if (_gpuBackoffUntil.TryUpdate(monitorId, expiration, current))
                {
                    return true;
                }

                continue;
            }

            if (_gpuBackoffUntil.TryAdd(monitorId, expiration))
            {
                return true;
            }
        }
    }

    public static bool IsGpuInBackoff(string? monitorId)
    {
        if (IsGpuGloballyDisabled)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(monitorId))
        {
            return false;
        }

        if (!_gpuBackoffUntil.TryGetValue(monitorId, out var until))
        {
            return false;
        }

        if (until <= DateTime.UtcNow)
        {
            _gpuBackoffUntil.TryRemove(monitorId, out _);
            return false;
        }

        return true;
    }

    public static bool IsGpuGloballyDisabled => Volatile.Read(ref _gpuGloballyDisabled) == 1;

    public static bool DisableGpuGlobally()
        => Interlocked.Exchange(ref _gpuGloballyDisabled, 1) == 0;

#if WINDOWS10_0_19041_0_OR_GREATER
    [SupportedOSPlatform("windows10.0.19041")]
    private static void EnableCursorCapture(GraphicsCaptureSession session)
    {
        session.IsCursorCaptureEnabled = true;
    }
#else
    [SupportedOSPlatform("windows10.0.19041")]
    private static void EnableCursorCapture(GraphicsCaptureSession session)
    {
        // Cursor capture is not available on this target contract.
    }
#endif

    /// <inheritdoc />
    [SupportedOSPlatform("windows10.0.17763")]
    public ValueTask StopAsync()
    {
        lock (_gate)
        {
            if (_framePool is not null)
            {
                _framePool.FrameArrived -= OnFrameArrived;
                _framePool.Dispose();
                _framePool = null;
            }

            if (_session is not null)
            {
                _session.Dispose();
                _session = null;
            }

            _captureItem = null;
            ReleaseDirect3D();
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    [SupportedOSPlatform("windows10.0.17763")]
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    [SupportedOSPlatform("windows10.0.17763")]
    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        Direct3D11CaptureFrame? frame = null;

        try
        {
            frame = sender.TryGetNextFrame();
            if (frame is null)
            {
                return;
            }

            if (frame.ContentSize.Width <= 0 || frame.ContentSize.Height <= 0)
            {
                return;
            }

            if (!frame.ContentSize.Equals(_currentSize) && _framePool is not null && _captureItem is not null)
            {
                _currentSize = frame.ContentSize;
                sender.Recreate(
                    _direct3DDevice!,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    FramePoolBufferCount,
                    _currentSize);
            }

            using var texture = CreateTextureFromSurface(frame.Surface);
            var bitmap = CopyTextureToBitmap(texture, frame.ContentSize.Width, frame.ContentSize.Height);
            DispatchFrame(bitmap);
        }
        catch
        {
            // Ignore transient frame failures.
        }
        finally
        {
            frame?.Dispose();
        }
    }

    [SupportedOSPlatform("windows10.0.17763")]
    private void InitializeDirect3D()
    {
        if (_d3dDevice is not null)
        {
            return;
        }

        const DeviceCreationFlags creationFlags = DeviceCreationFlags.BgraSupport;
        var featureLevels = new[]
        {
            FeatureLevel.Level_11_1,
            FeatureLevel.Level_11_0,
        };

        var result = D3D11.D3D11CreateDevice(
            null,
            DriverType.Hardware,
            creationFlags,
            featureLevels,
            out var device,
            out _,
            out var context);

        if (result.Failure)
        {
            result = D3D11.D3D11CreateDevice(
                null,
                DriverType.Warp,
                creationFlags,
                featureLevels,
                out device,
                out _,
                out context);
        }

        result.CheckError();

        _d3dDevice = device ?? throw new InvalidOperationException("Failed to create a Direct3D device for capture.");
        _d3dContext = context ?? throw new InvalidOperationException("Failed to create a Direct3D context for capture.");

        using var dxgiDevice = _d3dDevice.QueryInterface<IDXGIDevice>();
        _direct3DDevice = Direct3D11Helper.CreateDevice(dxgiDevice)
            ?? throw new InvalidOperationException("Failed to create a Windows Graphics Capture device wrapper.");
    }

    [SupportedOSPlatform("windows10.0.17763")]
    private void ReleaseDirect3D()
    {
        _direct3DDevice?.Dispose();
        _direct3DDevice = null;

        _d3dContext?.ClearState();
        _d3dContext?.Dispose();
        _d3dContext = null;

        _d3dDevice?.Dispose();
        _d3dDevice = null;
    }

    [SupportedOSPlatform("windows10.0.17763")]
    private Vortice.Direct3D11.ID3D11Texture2D CreateTextureFromSurface(Windows.Graphics.DirectX.Direct3D11.IDirect3DSurface surface)
    {
        var textureGuid = typeof(Vortice.Direct3D11.ID3D11Texture2D).GUID;
        var nativeResource = GraphicsCaptureInterop.GetInterfaceFromSurface(surface, textureGuid);
        return new Vortice.Direct3D11.ID3D11Texture2D(nativeResource);
    }

    [SupportedOSPlatform("windows10.0.17763")]
    private Bitmap CopyTextureToBitmap(Vortice.Direct3D11.ID3D11Texture2D texture, int width, int height)
    {
        var description = texture.Description;

        var stagingDesc = new Vortice.Direct3D11.Texture2DDescription
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = description.Format,
            SampleDescription = new Vortice.DXGI.SampleDescription(1, 0),
            Usage = Vortice.Direct3D11.ResourceUsage.Staging,
            CPUAccessFlags = Vortice.Direct3D11.CpuAccessFlags.Read,
            BindFlags = Vortice.Direct3D11.BindFlags.None,
            MiscFlags = Vortice.Direct3D11.ResourceOptionFlags.None,
        };

        using var staging = _d3dDevice!.CreateTexture2D(stagingDesc);
        _d3dContext!.CopyResource(texture, staging);

        var dataBox = _d3dContext.Map(staging, 0, Vortice.Direct3D11.MapMode.Read, Vortice.Direct3D11.MapFlags.None);

        try
        {
            var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var bitmapData = bitmap.LockBits(
                new Rectangle(0, 0, width, height),
                System.Drawing.Imaging.ImageLockMode.WriteOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            try
            {
                unsafe
                {
                    var sourcePtr = (byte*)dataBox.DataPointer;
                    var destinationPtr = (byte*)bitmapData.Scan0;
                    var bytesPerRow = width * 4;

                    for (var y = 0; y < height; y++)
                    {
                        Buffer.MemoryCopy(sourcePtr, destinationPtr, bitmapData.Stride, bytesPerRow);
                        sourcePtr += dataBox.RowPitch;
                        destinationPtr += bitmapData.Stride;
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }

            return bitmap;
        }
        finally
        {
            _d3dContext.Unmap(staging, 0);
        }
    }

    [SupportedOSPlatform("windows10.0.17763")]
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

#else
    public event EventHandler<MonitorFrameArrivedEventArgs>? FrameArrived
    {
        add { }
        remove { }
    }

    public bool IsSupported => false;

    public Task StartAsync(MonitorInfo monitor, CancellationToken cancellationToken = default)
        => throw new PlatformNotSupportedException();

    public ValueTask StopAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public static bool MarkGpuBackoff(string monitorId, TimeSpan? durationOverride = null) => false;

    public static bool IsGpuInBackoff(string? monitorId) => false;

    public static bool IsGpuGloballyDisabled => false;

    public static bool DisableGpuGlobally() => false;
#endif

    /// <summary>
    /// Gets a value indicating whether Windows Graphics Capture APIs are available.
    /// </summary>
#if WINDOWS10_0_17763_0_OR_GREATER
    [SupportedOSPlatform("windows10.0.17763")]
    public static bool IsGraphicsCaptureAvailable
        => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763) && Windows.Graphics.Capture.GraphicsCaptureSession.IsSupported();
#else
    public static bool IsGraphicsCaptureAvailable => false;
#endif
}
