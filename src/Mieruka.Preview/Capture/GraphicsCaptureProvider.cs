using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Mieruka.Core.Models;
#if WINDOWS10_0_17763_0_OR_GREATER
using Mieruka.Preview.Capture.Interop;
using SharpGen.Runtime;
#endif

namespace Mieruka.Preview;

/// <summary>
/// Monitor capture provider that uses Windows.Graphics.Capture APIs.
/// </summary>
[SupportedOSPlatform("windows10.0.17763")]
public sealed class GraphicsCaptureProvider : IMonitorCapture
{
#if WINDOWS10_0_17763_0_OR_GREATER
    private const int FramePoolBufferCount = 4;

    private readonly object _gate = new();
    private Windows.Graphics.Capture.Direct3D11CaptureFramePool? _framePool;
    private Windows.Graphics.Capture.GraphicsCaptureSession? _session;
    private Windows.Graphics.Capture.GraphicsCaptureItem? _captureItem;
    private Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice? _direct3DDevice;
    private Vortice.Direct3D11.ID3D11Device? _d3dDevice;
    private Vortice.Direct3D11.ID3D11DeviceContext? _d3dContext;
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

        if (!IsGraphicsCaptureAvailable)
        {
            throw new PlatformNotSupportedException("Windows Graphics Capture is not supported on this system.");
        }

        if (monitor.DeviceName is null)
        {
            throw new ArgumentException("Monitor device name is not defined.", nameof(monitor));
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
            }
            catch
            {
                ReleaseDirect3D();
                throw;
            }
            _captureItem = GraphicsCaptureInterop.CreateItemForMonitor(monitorHandle);
            _currentSize = _captureItem.Size;

            _framePool = Windows.Graphics.Capture.Direct3D11CaptureFramePool.CreateFreeThreaded(
                _direct3DDevice!,
                Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized,
                FramePoolBufferCount,
                _captureItem.Size);

            _framePool.FrameArrived += OnFrameArrived;

            _session = _framePool.CreateCaptureSession(_captureItem);
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            {
                ConfigureSessionOptions(_session);
            }
            _session.StartCapture();

            // ensure bounds stored so fallback can use if needed
            _ = bounds;
        }

        return Task.CompletedTask;
    }

#if WINDOWS10_0_19041_0_OR_GREATER
    [SupportedOSPlatform("windows10.0.19041")]
    private static void ConfigureSessionOptions(Windows.Graphics.Capture.GraphicsCaptureSession session)
    {
        session.IsCursorCaptureEnabled = false;
        session.IsBorderRequired = false;
    }
#else
    [SupportedOSPlatform("windows10.0.19041")]
    private static void ConfigureSessionOptions(Windows.Graphics.Capture.GraphicsCaptureSession session)
    {
        // Cursor capture configuration is not available on this target contract.
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
    private void OnFrameArrived(Windows.Graphics.Capture.Direct3D11CaptureFramePool sender, object args)
    {
        Windows.Graphics.Capture.Direct3D11CaptureFrame? frame = null;

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
                    Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized,
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

        var creationFlags = Vortice.Direct3D11.DeviceCreationFlags.BgraSupport | Vortice.Direct3D11.DeviceCreationFlags.VideoSupport;
        var featureLevels = new[]
        {
            Vortice.Direct3D.FeatureLevel.Level_12_1,
            Vortice.Direct3D.FeatureLevel.Level_12_0,
            Vortice.Direct3D.FeatureLevel.Level_11_1,
            Vortice.Direct3D.FeatureLevel.Level_11_0,
            Vortice.Direct3D.FeatureLevel.Level_10_1,
            Vortice.Direct3D.FeatureLevel.Level_10_0,
        };

        var result = Vortice.Direct3D11.D3D11.D3D11CreateDevice(
            IntPtr.Zero,
            Vortice.Direct3D.DriverType.Hardware,
            creationFlags,
            featureLevels,
            out var device,
            out var featureLevel,
            out var context);

        if (result.Failure)
        {
            result = Vortice.Direct3D11.D3D11.D3D11CreateDevice(
                IntPtr.Zero,
                Vortice.Direct3D.DriverType.Warp,
                creationFlags,
                featureLevels,
                out device,
                out featureLevel,
                out context);
        }

        result.CheckError();

        if (device is null || context is null)
        {
            throw new InvalidOperationException("Failed to create a Direct3D device context for capture.");
        }

        _ = featureLevel;

        _d3dDevice = device;
        _d3dContext = context;

        using var dxgiDevice = _d3dDevice.QueryInterface<Vortice.DXGI.IDXGIDevice>();
        var hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var graphicsDevice);
        Marshal.ThrowExceptionForHR(hr);
        if (graphicsDevice == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create a Direct3D device for capture.");
        }
        try
        {
            _direct3DDevice = GraphicsCaptureInterop.CreateDirect3DDevice(graphicsDevice);
        }
        finally
        {
            Marshal.Release(graphicsDevice);
        }
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

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);
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
