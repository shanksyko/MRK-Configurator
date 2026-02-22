using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Mieruka.Core.Config;
using Mieruka.Core.Models;
using Mieruka.Core.Diagnostics;
#if WINDOWS10_0_17763_0_OR_GREATER
using Mieruka.Preview.Capture.Interop;
using Mieruka.Preview.Diagnostics;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Foundation;
#endif
using Serilog;

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
    private static readonly ILogger Logger = Log.ForContext<GraphicsCaptureProvider>();
#if DEBUG
    private static readonly AsyncLocal<int> _dispatchDepth = new();
#endif
    private static int _gpuGloballyDisabled;
    private const string Backend = "WGC";

    private const int MinFramePoolBufferCount = 2;
    private const int MaxFramePoolBufferCount = 3;
    private const int DxgiErrorDeviceRemoved = unchecked((int)0x887A0005);
    private const int DxgiErrorDeviceReset = unchecked((int)0x887A0007);
    private const int DxgiErrorUnsupported = unchecked((int)0x887A0004);
    private const DirectXPixelFormat CapturePixelFormat = DirectXPixelFormat.B8G8R8A8UIntNormalized;

    private readonly object _gate = new();
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private GraphicsCaptureItem? _captureItem;
    private int _fatalFailureSignaled;
    private IDirect3DDevice? _direct3DDevice;
    private ID3D11Device? _d3dDevice;
    private ID3D11DeviceContext? _d3dContext;
    private Windows.Graphics.SizeInt32 _currentSize;
    private string? _monitorId;
    private ILogger? _captureLogger;
    private string? _previewSessionId;
    private readonly Stopwatch _captureStopwatch = new();
    private PreviewFrameScheduler? _frameScheduler;
    private PreviewResolution? _previewResolution;
    private long _frames;
    private long _dropped;
    private long _invalidImages;
    private long _totalFrames;
    private long _totalDropped;
    private long _totalInvalidImages;
    private long _lastStatsTick = Environment.TickCount64;
    private FeatureLevel _selectedFeatureLevel = FeatureLevel.Level_11_0;
    private ID3D11Texture2D? _stagingTexture;
    private int _stagingWidth;
    private int _stagingHeight;
    private static readonly ConcurrentDictionary<Type, MethodInfo?> _closeMethodCache = new();

    /// <inheritdoc />
    public event EventHandler<MonitorFrameArrivedEventArgs>? FrameArrived;

    /// <inheritdoc />
    [SupportedOSPlatform("windows10.0.17763")]
    public bool IsSupported => GpuCaptureGuard.CanUseGpu() && IsGraphicsCaptureAvailable;

    /// <inheritdoc />
    [SupportedOSPlatform("windows10.0.17763")]
    public Task StartAsync(MonitorInfo monitor, CancellationToken cancellationToken = default)
    {
        using var guard = new StackGuard(nameof(StartAsync));
        if (!guard.Entered)
        {
            return Task.CompletedTask;
        }

        ArgumentNullException.ThrowIfNull(monitor);

        cancellationToken.ThrowIfCancellationRequested();

        if (!GpuCaptureGuard.CanUseGpu())
        {
            throw new GraphicsCaptureUnavailableException("GPU disabled by guard.", isPermanent: true);
        }

        if (IsGpuGloballyDisabled)
        {
            throw new GraphicsCaptureUnavailableException("Windows Graphics Capture foi desativado para esta sessão.", isPermanent: true);
        }

        var monitorDescriptor = monitor.DeviceName ?? monitor.Id ?? monitor.Name ?? "<unknown>";

        var compatibility = WgcDiagnosticTool.GetReport();
        if (compatibility.Warnings.Length > 0)
        {
            foreach (var warning in compatibility.Warnings)
            {
                Logger.Warning(
                    "Diagnóstico WGC gerou aviso para {Monitor}: {Warning}",
                    monitorDescriptor,
                    warning);
            }
        }

        if (!compatibility.IsSupported)
        {
            var message = WgcDiagnosticTool.BuildFailureMessage(compatibility);
            Logger.Warning(
                "Windows Graphics Capture desabilitado antes do start para {Monitor}: {Reason}",
                monitorDescriptor,
                message);
            throw new GraphicsCaptureUnavailableException(message, WgcDiagnosticTool.ShouldTreatAsPermanent(compatibility));
        }

        if (monitor.DeviceName is null)
        {
            throw new ArgumentException("Monitor device name is not defined.", nameof(monitor));
        }

        var monitorKey = MonitorIdentifier.Create(monitor);
        if (string.IsNullOrWhiteSpace(monitorKey))
        {
            monitorKey = monitor.Id;
        }

        if (string.IsNullOrWhiteSpace(monitorKey))
        {
            monitorKey = monitor.DeviceName;
        }

        if (string.IsNullOrWhiteSpace(monitorKey))
        {
            monitorKey = monitor.Name;
        }

        if (string.IsNullOrWhiteSpace(monitorKey))
        {
            monitorKey = "<unknown>";
        }

        if (IsGpuInBackoff(monitorKey))
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

            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                throw new GraphicsCaptureUnavailableException(
                    "Monitor sem área visível para captura (minimizado ou desconectado).",
                    isPermanent: false);
            }

            try
            {
                _monitorId = monitorKey;
                InitializeDirect3D();

                if (!GraphicsCaptureInterop.TryCreateItemForMonitor(monitorHandle, out var captureItem, out var creationFailure))
                {
                    if (creationFailure is not null)
                    {
                        throw creationFailure;
                    }

                    throw new GraphicsCaptureUnavailableException(
                        "Falha desconhecida ao criar GraphicsCaptureItem para o monitor informado.",
                        isPermanent: false);
                }

                var captureItemInstance = captureItem
                    ?? throw new InvalidOperationException(
                        "GraphicsCaptureProvider: capture item is null before starting capture.");

                _captureItem = captureItemInstance;
                captureItemInstance.Closed += OnCaptureItemClosed;
                var rawSize = captureItemInstance.Size;
                _currentSize = SanitizeContentSize(rawSize, monitor.DeviceName);

                if (rawSize.Width <= 0 || rawSize.Height <= 0 || _currentSize.Width <= 0 || _currentSize.Height <= 0)
                {
                    throw new GraphicsCaptureUnavailableException(
                        "Windows Graphics Capture retornou um item sem área visível (provável monitor minimizado).",
                        isPermanent: false);
                }

                var bufferCount = ClampFramePoolBufferCount(MaxFramePoolBufferCount);
                Logger.Debug(
                    "Criando frame pool WGC para {Monitor} com {Width}x{Height} e {Buffers} buffers.",
                    monitor.DeviceName,
                    _currentSize.Width,
                    _currentSize.Height,
                    bufferCount);

                if (_currentSize.Width <= 0 || _currentSize.Height <= 0)
                {
                    throw new GraphicsCaptureUnavailableException(
                        "Dimensões do frame pool inválidas após sanitização.",
                        isPermanent: false);
                }

                _framePool = CreateFramePool(bufferCount, _currentSize);
                var framePool = _framePool
                    ?? throw new InvalidOperationException(
                        "GraphicsCaptureProvider: frame pool creation returned null.");

                _frameScheduler = new PreviewFrameScheduler(PreviewSettings.Default.TargetFpsGpu);
                framePool.FrameArrived += OnFrameArrived;

                var captureItemForSession = _captureItem
                    ?? throw new InvalidOperationException(
                        "GraphicsCaptureProvider: capture item unexpectedly released before creating capture session.");
                _session = framePool.CreateCaptureSession(captureItemForSession);
                if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
                {
                    EnableCursorCapture(_session);
                }
                _session.StartCapture();

                // ensure bounds stored so fallback can use if needed
                _ = bounds;

                _monitorId = monitorKey;
                _previewSessionId = Guid.NewGuid().ToString("N");
                _captureLogger = Logger
                    .ForMonitor(monitorKey)
                    .ForContext("MonitorId", _monitorId)
                    .ForContext("Backend", Backend)
                    .ForContext("PreviewSessionId", _previewSessionId);
                _captureStopwatch.Restart();
                _previewResolution = monitor.GetPreviewResolution();
                Interlocked.Exchange(ref _frames, 0);
                Interlocked.Exchange(ref _dropped, 0);
                Interlocked.Exchange(ref _invalidImages, 0);
                Interlocked.Exchange(ref _totalFrames, 0);
                Interlocked.Exchange(ref _totalDropped, 0);
                Interlocked.Exchange(ref _totalInvalidImages, 0);
                _lastStatsTick = Environment.TickCount64;

                _captureLogger.Information(
                    "PreviewStart: backend={Backend}, monitorId={MonitorId}, previewSessionId={PreviewSessionId}, featureLevel={Level}, targetFps={TargetFps:F1}",
                    Backend,
                    _monitorId,
                    _previewSessionId,
                    _selectedFeatureLevel,
                    _frameScheduler.TargetFps);
            }
            catch (GraphicsCaptureUnavailableException)
            {
                CleanupAfterFailure();
                throw;
            }
            catch (COMException ex)
            {
                var failure = TranslateAndWrap("StartCapture", ex);
                CleanupAfterFailure();
                throw failure;
            }
            catch (Exception ex)
            {
                CleanupAfterFailure();
                Logger.Error(ex, "Falha inesperada ao inicializar captura WGC para {Monitor}.", monitor.DeviceName);
                throw new GraphicsCaptureUnavailableException(
                    "Falha inesperada ao inicializar Windows Graphics Capture.",
                    isPermanent: false,
                    ex);
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
        using var guard = new StackGuard(nameof(StopAsync));
        if (!guard.Entered)
        {
            return ValueTask.CompletedTask;
        }

        lock (_gate)
        {
            CleanupAfterFailure();
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    [SupportedOSPlatform("windows10.0.17763")]
    public async ValueTask DisposeAsync()
    {
        using var guard = new StackGuard(nameof(DisposeAsync));
        if (!guard.Entered)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
    }

    [SupportedOSPlatform("windows10.0.17763")]
    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        using var guard = new StackGuard(nameof(OnFrameArrived));
        if (!guard.Entered)
        {
            return;
        }

        Direct3D11CaptureFrame? frame = null;
        var scheduler = _frameScheduler;
        var frameProcessingStarted = false;
        long frameStartTimestamp = 0;

        try
        {
            var shouldProcess = scheduler?.TryBeginFrame(out frameStartTimestamp) ?? true;
            frame = sender.TryGetNextFrame();
            if (frame is null)
            {
                if (shouldProcess)
                {
                    scheduler?.EndFrame(frameStartTimestamp);
                }
                RecordFrameDiscarded(invalidFrame: false);
                return;
            }

            if (!shouldProcess)
            {
                RecordFrameDiscarded(invalidFrame: false);
                return;
            }

            frameProcessingStarted = true;

            if (frame.ContentSize.Width <= 0 || frame.ContentSize.Height <= 0)
            {
                RecordFrameDiscarded(invalidFrame: true);
                Logger.Debug("Frame WGC com dimensões inválidas {Width}x{Height}; descartando.", frame.ContentSize.Width, frame.ContentSize.Height);
                return;
            }

            if (!frame.ContentSize.Equals(_currentSize) && _framePool is not null && _captureItem is not null)
            {
                var rawSize = frame.ContentSize;
                _currentSize = SanitizeContentSize(rawSize, _captureItem.DisplayName);
                if (rawSize.Width <= 0 || rawSize.Height <= 0)
                {
                    RecordFrameDiscarded(invalidFrame: true);
                    Logger.Debug("Frame pool reportou dimensões zero após resize; aguardando próximos frames.");
                    return;
                }

                if (_currentSize.Width <= 0 || _currentSize.Height <= 0)
                {
                    RecordFrameDiscarded(invalidFrame: true);
                    Logger.Debug("Dimensões sanitizadas inválidas após resize: {Width}x{Height}.", _currentSize.Width, _currentSize.Height);
                    return;
                }

                try
                {
                    sender.Recreate(
                        _direct3DDevice!,
                        CapturePixelFormat,
                        ClampFramePoolBufferCount(MaxFramePoolBufferCount),
                        _currentSize);
                }
                catch (COMException ex)
                {
                    RecordFrameDiscarded(invalidFrame: false);
                    HandleFrameFailure("RecreateFramePool", ex);
                    return;
                }
            }

            if (_d3dDevice is null || _d3dContext is null)
            {
                RecordFrameDiscarded(invalidFrame: false);
                Logger.Debug("Contexto D3D ausente durante processamento de frame; ignorando frame atual.");
                return;
            }

            using var texture = CreateTextureFromSurface(frame.Surface);
            var bitmap = CopyTextureToBitmap(texture, frame.ContentSize.Width, frame.ContentSize.Height);
            bitmap = ConvertToPreviewBitmap(bitmap);
            DispatchFrame(bitmap);
        }
        catch (COMException ex)
        {
            RecordFrameDiscarded(invalidFrame: false);
            HandleFrameFailure("FrameProcessing", ex);
        }
        catch (ObjectDisposedException ex)
        {
            RecordFrameDiscarded(invalidFrame: false);
            HandleFatalFrameFailure("FrameProcessingDisposed", ex);
        }
        catch (InvalidOperationException ex)
        {
            RecordFrameDiscarded(invalidFrame: false);
            HandleFatalFrameFailure("FrameProcessingInvalidOperation", ex);
        }
        catch (Exception ex)
        {
            RecordFrameDiscarded(invalidFrame: false);
            Logger.Debug(ex, "Falha transitória ao processar frame WGC.");
        }
        finally
        {
            if (frameProcessingStarted)
            {
                scheduler?.EndFrame(frameStartTimestamp);
            }

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
            out var selectedLevel,
            out var context);

        if (result.Failure)
        {
            Logger.Debug(
                "Falha ao criar device D3D11 com driver Hardware (0x{HResult:X8}); tentando WARP.",
                result.Code);
            result = D3D11.D3D11CreateDevice(
                null,
                DriverType.Warp,
                creationFlags,
                featureLevels,
                out device,
                out selectedLevel,
                out context);
        }

        if (result.Failure)
        {
            ThrowDeviceCreationFailure(result.Code, "D3D11CreateDevice");
        }

        _d3dDevice = device ?? throw new InvalidOperationException("Failed to create a Direct3D device for capture.");
        _d3dContext = context ?? throw new InvalidOperationException("Failed to create a Direct3D context for capture.");

        var featureLevel = selectedLevel;
        _selectedFeatureLevel = featureLevel;
        if (featureLevel < FeatureLevel.Level_11_0)
        {
            ReleaseDirect3D();
            throw new GraphicsCaptureUnavailableException(
                $"Windows Graphics Capture requer Direct3D 11; nível atual {featureLevel}.",
                isPermanent: true);
        }

        using var dxgiDevice = _d3dDevice.QueryInterface<IDXGIDevice>();
        _direct3DDevice = Direct3D11Helper.CreateDeviceFromDxgiDevice(dxgiDevice)
            ?? throw new InvalidOperationException("Failed to create a Windows Graphics Capture device wrapper.");

        Logger.Debug(
            "Device D3D11 criado ({FeatureLevel}) com contexto {ContextType}.",
            featureLevel,
            _d3dContext.GetType().FullName);
    }

    [SupportedOSPlatform("windows10.0.17763")]
    private void ReleaseDirect3D()
    {
        if (_stagingTexture is not null)
        {
            _stagingTexture.Dispose();
            _stagingTexture = null;
            _stagingWidth = 0;
            _stagingHeight = 0;
        }

        if (_direct3DDevice is not null)
        {
            Logger.Debug("Liberando wrapper Direct3D11 (tipo {Type}).", _direct3DDevice.GetType().FullName);
            DisposeComObject(ref _direct3DDevice);
        }

        if (_d3dContext is not null)
        {
            Logger.Debug("Liberando contexto D3D11 (tipo {Type}).", _d3dContext.GetType().FullName);
            _d3dContext.ClearState();
            _d3dContext.Dispose();
            _d3dContext = null;
        }

        if (_d3dDevice is not null)
        {
            Logger.Debug("Liberando device D3D11 (tipo {Type}).", _d3dDevice.GetType().FullName);
            _d3dDevice.Dispose();
            _d3dDevice = null;
        }
    }

    private static void DisposeComObject<T>(ref T? comObject)
        where T : class
    {
        var instance = Interlocked.Exchange(ref comObject, null);
        if (instance is null)
        {
            return;
        }

        var type = instance.GetType();

        try
        {
            var closeMethod = _closeMethodCache.GetOrAdd(type, static t => t.GetMethod("Close", Type.EmptyTypes));
            if (closeMethod is not null)
            {
                closeMethod.Invoke(instance, null);
            }
        }
        catch (TargetInvocationException ex)
        {
            Logger.Debug(ex.InnerException ?? ex, "Close() falhou para recurso COM {Type}.", type.FullName);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Close() falhou para recurso COM {Type}.", type.FullName);
        }

        try
        {
            if (instance is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidCastException or COMException)
        {
            Logger.Debug(ex, "Dispose falhou para recurso COM {Type}; tentativa de liberação final continuará.", type.FullName);
        }
        catch (TargetInvocationException ex)
        {
            Logger.Debug(ex.InnerException ?? ex, "Dispose falhou para recurso COM {Type}.", type.FullName);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Falha inesperada ao liberar recurso COM {Type} via IDisposable.", type.FullName);
        }

        try
        {
            if (Marshal.IsComObject(instance))
            {
                Marshal.FinalReleaseComObject(instance);
            }
        }
        catch (InvalidCastException ex)
        {
            Logger.Debug(ex, "FinalReleaseComObject falhou para recurso COM {Type}.", type.FullName);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Falha ao liberar recurso COM {Type} via FinalReleaseComObject.", type.FullName);
        }
    }

    private void CleanupAfterFailure()
    {
        var hadCapture = _captureLogger is not null || _captureStopwatch.IsRunning;
        if (hadCapture)
        {
            MaybePublishStats(force: true);
        }
        else
        {
            Interlocked.Exchange(ref _frames, 0);
            Interlocked.Exchange(ref _dropped, 0);
            Interlocked.Exchange(ref _invalidImages, 0);
        }

        if (_captureStopwatch.IsRunning)
        {
            _captureStopwatch.Stop();
            var logger = _captureLogger ?? Logger;
            logger.Information(
                "PreviewStop: backend={Backend}, monitorId={MonitorId}, previewSessionId={PreviewSessionId}, uptimeMs={Uptime}, frames={Frames}, dropped={Dropped}, invalid={Invalid}",
                Backend,
                _monitorId,
                _previewSessionId,
                _captureStopwatch.ElapsedMilliseconds,
                Interlocked.Read(ref _totalFrames),
                Interlocked.Read(ref _totalDropped),
                Interlocked.Read(ref _totalInvalidImages));
            _captureStopwatch.Reset();
        }

        _captureLogger = null;
        _previewSessionId = null;
        _frameScheduler = null;

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

        if (_captureItem is not null)
        {
            _captureItem.Closed -= OnCaptureItemClosed;
        }

        _captureItem = null;
        _monitorId = null;
        _previewResolution = null;
        Interlocked.Exchange(ref _fatalFailureSignaled, 0);
        ReleaseDirect3D();

        Interlocked.Exchange(ref _frames, 0);
        Interlocked.Exchange(ref _dropped, 0);
        Interlocked.Exchange(ref _invalidImages, 0);
        Interlocked.Exchange(ref _totalFrames, 0);
        Interlocked.Exchange(ref _totalDropped, 0);
        Interlocked.Exchange(ref _totalInvalidImages, 0);
    }

    private void RecordFrameProduced()
    {
        Interlocked.Increment(ref _frames);
        Interlocked.Increment(ref _totalFrames);
        MaybePublishStats();
    }

    private void RecordFrameDiscarded(bool invalidFrame)
    {
        Interlocked.Increment(ref _dropped);
        Interlocked.Increment(ref _totalDropped);
        if (invalidFrame)
        {
            Interlocked.Increment(ref _invalidImages);
            Interlocked.Increment(ref _totalInvalidImages);
        }

        MaybePublishStats();
    }

    private void MaybePublishStats(bool force = false)
    {
        var nowTick = Environment.TickCount64;
        if (!force && (nowTick - _lastStatsTick) < 5000)
        {
            return;
        }

        var frames = Interlocked.Exchange(ref _frames, 0);
        var dropped = Interlocked.Exchange(ref _dropped, 0);
        var invalid = Interlocked.Exchange(ref _invalidImages, 0);

        if (!force && frames == 0 && dropped == 0 && invalid == 0)
        {
            return;
        }

        var elapsedSeconds = Math.Max(0.001, (nowTick - _lastStatsTick) / 1000.0);
        _lastStatsTick = nowTick;

        var fps = frames / elapsedSeconds;
        var logger = _captureLogger ?? Logger;
        var schedulerMetrics = _frameScheduler?.GetMetricsAndReset();
        logger.Debug(
            "PreviewStats: backend={Backend}, monitorId={MonitorId}, previewSessionId={PreviewSessionId}, fps={Fps:F1}, targetFps={TargetFps:F1}, frames={Frames}, dropped={Dropped}, invalid={Invalid}, totalFrames={TotalFrames}, totalDropped={TotalDropped}, totalInvalid={TotalInvalid}, processingMs={ProcessingMs:F2}",
            Backend,
            _monitorId,
            _previewSessionId,
            fps,
            _frameScheduler?.TargetFps,
            frames,
            dropped,
            invalid,
            Interlocked.Read(ref _totalFrames),
            Interlocked.Read(ref _totalDropped),
            Interlocked.Read(ref _totalInvalidImages),
            schedulerMetrics?.AverageProcessingMilliseconds ?? 0);
    }

    private Direct3D11CaptureFramePool CreateFramePool(int bufferCount, Windows.Graphics.SizeInt32 size)
    {
        if (_direct3DDevice is null)
        {
            throw new InvalidOperationException("Direct3D device was not initialized.");
        }

        if (size.Width <= 0 || size.Height <= 0)
        {
            throw new GraphicsCaptureUnavailableException(
                "Dimensões inválidas ao criar frame pool WGC.",
                isPermanent: false);
        }

        try
        {
            return Direct3D11CaptureFramePool.CreateFreeThreaded(
                _direct3DDevice,
                CapturePixelFormat,
                ClampFramePoolBufferCount(bufferCount),
                size);
        }
        catch (COMException ex)
        {
            throw TranslateAndWrap("CreateFramePool", ex);
        }
    }

    private static Windows.Graphics.SizeInt32 SanitizeContentSize(Windows.Graphics.SizeInt32 size, string? monitorName)
    {
        var width = size.Width;
        var height = size.Height;

        var sanitizedWidth = ClampDimension(width);
        var sanitizedHeight = ClampDimension(height);

        if (sanitizedWidth != width || sanitizedHeight != height)
        {
            Logger.Debug(
                "Normalizando dimensões de captura {Width}x{Height} para {Monitor} => {SanitizedWidth}x{SanitizedHeight}.",
                width,
                height,
                monitorName ?? "desconhecido",
                sanitizedWidth,
                sanitizedHeight);
        }

        return new Windows.Graphics.SizeInt32
        {
            Width = sanitizedWidth,
            Height = sanitizedHeight,
        };
    }

    private static int ClampDimension(int value)
    {
        if (value < 1)
        {
            return 1;
        }

        const int MaxDimension = ushort.MaxValue;
        if (value > MaxDimension)
        {
            return MaxDimension;
        }

        return value;
    }

    private static int ClampFramePoolBufferCount(int requested)
    {
        if (requested < MinFramePoolBufferCount)
        {
            return MinFramePoolBufferCount;
        }

        if (requested > MaxFramePoolBufferCount)
        {
            return MaxFramePoolBufferCount;
        }

        return requested;
    }

    private GraphicsCaptureUnavailableException TranslateAndWrap(string stage, COMException exception)
    {
        var hresult = exception.HResult;
        var permanent = hresult switch
        {
            DxgiErrorUnsupported => true,
            _ => false,
        };

        Logger.Warning(
            exception,
            "Windows Graphics Capture falhou em {Stage} (HRESULT 0x{HResult:X8}).",
            stage,
            hresult);

        if (!permanent && _monitorId is not null)
        {
            MarkGpuBackoff(_monitorId);
        }

        return new GraphicsCaptureUnavailableException(
            $"Windows Graphics Capture falhou em {stage} (HRESULT 0x{hresult:X8}).",
            permanent,
            exception);
    }

    private void HandleFrameFailure(string stage, COMException exception)
    {
        Logger.Debug(
            exception,
            "Falha COM durante {Stage} (HRESULT 0x{HResult:X8}).",
            stage,
            exception.HResult);

        if (exception.HResult is DxgiErrorDeviceRemoved or DxgiErrorDeviceReset)
        {
            Logger.Warning(
                exception,
                "Device D3D11 foi perdido durante {Stage}; interrompendo captura e ativando fallback.",
                stage);

            if (_monitorId is not null)
            {
                MarkGpuBackoff(_monitorId);
            }

            lock (_gate)
            {
                CleanupAfterFailure();
            }
        }
    }

    private void HandleFatalFrameFailure(string stage, Exception exception)
    {
        if (Interlocked.Exchange(ref _fatalFailureSignaled, 1) == 1)
        {
            return;
        }

        Logger.Warning(
            exception,
            "Captura WGC encerrada por falha irreversível em {Stage} para monitor {MonitorId}.",
            stage,
            _monitorId ?? "<unknown>");

        if (_monitorId is not null)
        {
            MarkGpuBackoff(_monitorId);
        }

        lock (_gate)
        {
            CleanupAfterFailure();
        }
    }

    private void OnCaptureItemClosed(GraphicsCaptureItem sender, object args)
    {
        HandleFatalFrameFailure(
            "GraphicsCaptureItemClosed",
            new InvalidOperationException("GraphicsCaptureItem was closed."));
    }

    private void ThrowDeviceCreationFailure(int hresult, string stage)
    {
        Logger.Error(
            "Falha ao criar recursos D3D11 ({Stage}) com HRESULT 0x{HResult:X8}.",
            stage,
            hresult);

        var permanent = hresult switch
        {
            DxgiErrorUnsupported => true,
            _ => false,
        };

        throw new GraphicsCaptureUnavailableException(
            $"Falha ao inicializar Direct3D (HRESULT 0x{hresult:X8} em {stage}).",
            permanent,
            new COMException($"D3D falhou com HRESULT 0x{hresult:X8}.", hresult));
    }

    [SupportedOSPlatform("windows10.0.17763")]
    private Vortice.Direct3D11.ID3D11Texture2D CreateTextureFromSurface(Windows.Graphics.DirectX.Direct3D11.IDirect3DSurface surface)
    {
        var textureGuid = typeof(Vortice.Direct3D11.ID3D11Texture2D).GUID;
        var nativeResource = GraphicsCaptureInterop.GetInterfaceFromSurface(surface, textureGuid);
        return new Vortice.Direct3D11.ID3D11Texture2D(nativeResource);
    }

    [SupportedOSPlatform("windows10.0.17763")]
    private ID3D11Texture2D GetOrCreateStagingTexture(int width, int height, Vortice.DXGI.Format format)
    {
        if (_stagingTexture is not null && _stagingWidth == width && _stagingHeight == height)
        {
            return _stagingTexture;
        }

        _stagingTexture?.Dispose();
        _stagingTexture = null;
        _stagingWidth = 0;
        _stagingHeight = 0;

        _stagingTexture = _d3dDevice!.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            CPUAccessFlags = CpuAccessFlags.Read,
            BindFlags = BindFlags.None,
            MiscFlags = ResourceOptionFlags.None,
        });
        _stagingWidth = width;
        _stagingHeight = height;
        return _stagingTexture;
    }

    [SupportedOSPlatform("windows10.0.17763")]
    private Bitmap CopyTextureToBitmap(Vortice.Direct3D11.ID3D11Texture2D texture, int width, int height)
    {
        var description = texture.Description;
        var safeWidth = ClampDimension(width);
        var safeHeight = ClampDimension(height);

        var staging = GetOrCreateStagingTexture(safeWidth, safeHeight, description.Format);
        _d3dContext!.CopyResource(texture, staging);

        var dataBox = _d3dContext.Map(staging, 0, Vortice.Direct3D11.MapMode.Read, Vortice.Direct3D11.MapFlags.None);

        try
        {
            var bitmap = new Bitmap(safeWidth, safeHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var bitmapData = bitmap.LockBits(
                new Rectangle(0, 0, safeWidth, safeHeight),
                System.Drawing.Imaging.ImageLockMode.WriteOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            try
            {
                unsafe
                {
                    var sourcePtr = (byte*)dataBox.DataPointer;
                    var destinationPtr = (byte*)bitmapData.Scan0;
                    var bytesPerRow = safeWidth * 4;

                    for (var y = 0; y < safeHeight; y++)
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

    private Bitmap ConvertToPreviewBitmap(Bitmap bitmap)
    {
        var target = _previewResolution;
        if (target is null || !target.HasValidSize)
        {
            return bitmap;
        }

        if (bitmap.Width == target.LogicalWidth && bitmap.Height == target.LogicalHeight)
        {
            return bitmap;
        }

        Bitmap? scaled = null;
        try
        {
            scaled = new Bitmap(target.LogicalWidth, target.LogicalHeight, bitmap.PixelFormat);
            using (var graphics = Graphics.FromImage(scaled))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighSpeed;
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
                graphics.PixelOffsetMode = PixelOffsetMode.HighSpeed;
                graphics.SmoothingMode = SmoothingMode.None;
                graphics.DrawImage(
                    bitmap,
                    new Rectangle(0, 0, scaled.Width, scaled.Height),
                    new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    GraphicsUnit.Pixel);
            }

            bitmap.Dispose();
            return scaled;
        }
        catch (Exception ex)
        {
            scaled?.Dispose();
            Logger.Debug(ex, "Failed to scale bitmap to preview resolution.");
            return bitmap;
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

        RecordFrameProduced();

        var args = new MonitorFrameArrivedEventArgs(bitmap, DateTimeOffset.UtcNow);

#if DEBUG
        var nextDepth = _dispatchDepth.Value + 1;
        _dispatchDepth.Value = nextDepth;
        try
        {
            if (nextDepth > 1)
            {
                Logger.Debug("DispatchDepth {Depth}", nextDepth);
            }

            handler(this, args);
        }
        catch (Exception)
        {
            bitmap.Dispose();
            throw;
        }
        finally
        {
            _dispatchDepth.Value = nextDepth - 1;
        }
#else
        try
        {
            handler(this, args);
        }
        catch (Exception)
        {
            bitmap.Dispose();
            throw;
        }
#endif
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
    public static bool IsGraphicsCaptureAvailable => WgcDiagnosticTool.GetReport().IsSupported;
#else
    public static bool IsGraphicsCaptureAvailable => false;
#endif
}
