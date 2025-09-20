using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Mieruka.Core.Interop;
using Mieruka.Core.Models;
using Mieruka.Core.Services;

namespace Mieruka.App.Services;

/// <summary>
/// Associates configuration entries with running windows and ensures they stay in the desired position.
/// </summary>
internal sealed class BindingService : IDisposable
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(1200);

    private readonly IDisplayService _displayService;
    private readonly ITelemetry _telemetry;
    private readonly object _gate = new();
    private readonly Dictionary<string, WindowBinding<AppConfig>> _appBindings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WindowBinding<SiteConfig>> _siteBindings = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _reapplyCancellation;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="BindingService"/> class.
    /// </summary>
    /// <param name="displayService">Service that provides monitor information.</param>
    /// <param name="telemetry">Telemetry sink used to log reposition operations.</param>
    public BindingService(IDisplayService displayService, ITelemetry? telemetry = null)
    {
        _displayService = displayService ?? throw new ArgumentNullException(nameof(displayService));
        _telemetry = telemetry ?? NullTelemetry.Instance;

        _displayService.TopologyChanged += OnTopologyChanged;
    }

    /// <summary>
    /// Associates an application configuration with a window handle.
    /// </summary>
    /// <param name="config">Configuration entry.</param>
    /// <param name="windowHandle">Handle of the window that should follow the configuration.</param>
    public void Bind(AppConfig config, IntPtr windowHandle)
    {
        ArgumentNullException.ThrowIfNull(config);
        EnsureNotDisposed();

        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException("Window handle must not be zero.", nameof(windowHandle));
        }

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        WindowConfig windowConfig;
        bool shouldTeleport;

        lock (_gate)
        {
            if (!_appBindings.TryGetValue(config.Id, out var binding))
            {
                binding = new WindowBinding<AppConfig>(config, windowHandle);
                _appBindings[config.Id] = binding;
                windowConfig = binding.WindowConfig;
                shouldTeleport = true;
                binding.ResetHandleChangedFlag();
            }
            else
            {
                var previousWindow = binding.WindowConfig;
                binding.Update(config, windowHandle);
                windowConfig = binding.WindowConfig;
                shouldTeleport = binding.HandleChanged || !Equals(previousWindow, windowConfig);
                binding.ResetHandleChangedFlag();
            }
        }

        if (shouldTeleport)
        {
            ApplyWindow(config.Id, "app", windowHandle, windowConfig);
        }
    }

    /// <summary>
    /// Associates a site configuration with a window handle.
    /// </summary>
    /// <param name="config">Configuration entry.</param>
    /// <param name="windowHandle">Handle of the window that should follow the configuration.</param>
    public void Bind(SiteConfig config, IntPtr windowHandle)
    {
        ArgumentNullException.ThrowIfNull(config);
        EnsureNotDisposed();

        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException("Window handle must not be zero.", nameof(windowHandle));
        }

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        WindowConfig windowConfig;
        bool shouldTeleport;

        lock (_gate)
        {
            if (!_siteBindings.TryGetValue(config.Id, out var binding))
            {
                binding = new WindowBinding<SiteConfig>(config, windowHandle);
                _siteBindings[config.Id] = binding;
                windowConfig = binding.WindowConfig;
                shouldTeleport = true;
                binding.ResetHandleChangedFlag();
            }
            else
            {
                var previousWindow = binding.WindowConfig;
                binding.Update(config, windowHandle);
                windowConfig = binding.WindowConfig;
                shouldTeleport = binding.HandleChanged || !Equals(previousWindow, windowConfig);
                binding.ResetHandleChangedFlag();
            }
        }

        if (shouldTeleport)
        {
            ApplyWindow(config.Id, "site", windowHandle, windowConfig);
        }
    }

    /// <summary>
    /// Attempts to reapply the window configuration for the specified entry.
    /// </summary>
    /// <param name="kind">Entry category.</param>
    /// <param name="id">Unique identifier of the entry.</param>
    /// <param name="handle">Handle currently associated with the entry.</param>
    /// <returns><c>true</c> when the window was repositioned; otherwise, <c>false</c>.</returns>
    public bool TryReapplyBinding(EntryKind kind, string id, out IntPtr handle)
    {
        EnsureNotDisposed();

        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Entry identifier cannot be empty.", nameof(id));
        }

        handle = IntPtr.Zero;

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        WindowConfig? window = null;
        string? kindName = null;

        lock (_gate)
        {
            switch (kind)
            {
                case EntryKind.Application when _appBindings.TryGetValue(id, out var appBinding):
                    handle = appBinding.WindowHandle;
                    window = appBinding.WindowConfig;
                    kindName = "app";
                    break;

                case EntryKind.Site when _siteBindings.TryGetValue(id, out var siteBinding):
                    handle = siteBinding.WindowHandle;
                    window = siteBinding.WindowConfig;
                    kindName = "site";
                    break;
            }
        }

        if (handle == IntPtr.Zero || window is null || string.IsNullOrWhiteSpace(kindName))
        {
            handle = IntPtr.Zero;
            return false;
        }

        ApplyWindow(id, kindName, handle, window);
        return true;
    }

    /// <summary>
    /// Reapplies the window configuration for all tracked bindings.
    /// </summary>
    public void ReapplyAllBindings()
    {
        EnsureNotDisposed();

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ReapplyAll();
    }

    /// <summary>
    /// Releases resources associated with the service.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _displayService.TopologyChanged -= OnTopologyChanged;

        lock (_gate)
        {
            _reapplyCancellation?.Cancel();
            _reapplyCancellation?.Dispose();
            _reapplyCancellation = null;

            _appBindings.Clear();
            _siteBindings.Clear();
        }

        GC.SuppressFinalize(this);
    }

    private void OnTopologyChanged(object? sender, EventArgs e)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        CancellationTokenSource? pending;

        lock (_gate)
        {
            _reapplyCancellation?.Cancel();
            _reapplyCancellation?.Dispose();

            if (_disposed)
            {
                return;
            }

            pending = new CancellationTokenSource();
            _reapplyCancellation = pending;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DebounceDelay, pending.Token).ConfigureAwait(false);

                if (!pending.Token.IsCancellationRequested)
                {
                    ReapplyAll();
                }
            }
            catch (TaskCanceledException)
            {
                // Expected when a new topology change event arrives before the delay elapses.
            }
        }, CancellationToken.None);
    }

    private void ReapplyAll()
    {
        List<WindowReapplyInfo> snapshot;

        lock (_gate)
        {
            snapshot = new List<WindowReapplyInfo>(_appBindings.Count + _siteBindings.Count);

            snapshot.AddRange(_appBindings.Select(pair =>
                new WindowReapplyInfo(pair.Key, "app", pair.Value.WindowHandle, pair.Value.WindowConfig)));

            snapshot.AddRange(_siteBindings.Select(pair =>
                new WindowReapplyInfo(pair.Key, "site", pair.Value.WindowHandle, pair.Value.WindowConfig)));
        }

        foreach (var item in snapshot)
        {
            ApplyWindow(item.Id, item.Kind, item.Handle, item.WindowConfig);
        }
    }

    private void ApplyWindow(string id, string kind, IntPtr handle, WindowConfig window)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var monitor = ResolveMonitor(window);
            if (monitor is null)
            {
                _telemetry.Warn($"Unable to find monitor for {kind} '{id}'.");
                return;
            }

            var monitorBounds = GetMonitorBounds(monitor);
            var targetBounds = CalculateBounds(window, monitor, monitorBounds);
            var topMost = window.AlwaysOnTop || window.FullScreen;

            WindowMover.MoveTo(handle, targetBounds, topMost, restoreIfMinimized: true);
            _telemetry.Info($"teleport {kind}:{id} {targetBounds.Left},{targetBounds.Top} {targetBounds.Width}x{targetBounds.Height}");
        }
        catch (Exception ex)
        {
            _telemetry.Error($"Failed to reposition window for {kind} '{id}'.", ex);
        }
    }

    private MonitorInfo? ResolveMonitor(WindowConfig window)
    {
        var monitor = _displayService.FindBy(window.Monitor);
        if (monitor is not null)
        {
            return monitor;
        }

        var all = _displayService.Monitors();
        return all.FirstOrDefault(m => m.IsPrimary) ?? all.FirstOrDefault();
    }

    private static Rectangle CalculateBounds(WindowConfig window, MonitorInfo monitor, Rectangle monitorBounds)
    {
        var scale = monitor.Scale > 0 ? monitor.Scale : 1.0;

        if (window.FullScreen)
        {
            return NormalizeBounds(monitorBounds, monitor);
        }

        var left = monitorBounds.Left;
        var top = monitorBounds.Top;

        if (window.X.HasValue)
        {
            left = monitorBounds.Left + ScaleValue(window.X.Value, scale);
        }

        if (window.Y.HasValue)
        {
            top = monitorBounds.Top + ScaleValue(window.Y.Value, scale);
        }

        var width = window.Width.HasValue
            ? ScaleValue(window.Width.Value, scale)
            : monitorBounds.Width;

        var height = window.Height.HasValue
            ? ScaleValue(window.Height.Value, scale)
            : monitorBounds.Height;

        if (width <= 0)
        {
            width = monitorBounds.Width;
        }

        if (height <= 0)
        {
            height = monitorBounds.Height;
        }

        return NormalizeBounds(new Rectangle(left, top, width, height), monitor);
    }

    private static Rectangle NormalizeBounds(Rectangle bounds, MonitorInfo monitor)
    {
        var width = bounds.Width <= 0 ? monitor.Width : bounds.Width;
        var height = bounds.Height <= 0 ? monitor.Height : bounds.Height;
        return new Rectangle(bounds.Left, bounds.Top, width, height);
    }

    private static int ScaleValue(int value, double scale)
        => (int)Math.Round(value * scale, MidpointRounding.AwayFromZero);

    private static Rectangle GetMonitorBounds(MonitorInfo monitor)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new Rectangle(0, 0, monitor.Width, monitor.Height);
        }

        if (!string.IsNullOrWhiteSpace(monitor.DeviceName) && TryGetDeviceBounds(monitor.DeviceName, out var bounds))
        {
            return bounds;
        }

        if (!string.IsNullOrWhiteSpace(monitor.Key.DeviceId) && TryGetDeviceBounds(monitor.Key.DeviceId, out bounds))
        {
            return bounds;
        }

        return new Rectangle(0, 0, monitor.Width, monitor.Height);
    }

    private static bool TryGetDeviceBounds(string deviceName, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var devMode = new DEVMODE
        {
            dmSize = (short)Marshal.SizeOf<DEVMODE>(),
        };

        if (!EnumDisplaySettingsEx(deviceName, EnumCurrentSettings, ref devMode, 0))
        {
            return false;
        }

        bounds = new Rectangle(devMode.dmPositionX, devMode.dmPositionY, devMode.dmPelsWidth, devMode.dmPelsHeight);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    private void EnsureNotDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(BindingService));
        }
    }

    private sealed class WindowBinding<T>
    {
        public WindowBinding(T config, IntPtr handle)
        {
            Config = config;
            WindowHandle = handle;
            HandleChanged = true;
        }

        public T Config { get; private set; }

        public IntPtr WindowHandle { get; private set; }

        public WindowConfig WindowConfig => Config switch
        {
            AppConfig app => app.Window,
            SiteConfig site => site.Window,
            _ => throw new InvalidOperationException("Unsupported configuration type."),
        };

        public bool HandleChanged { get; private set; }

        public void Update(T config, IntPtr handle)
        {
            Config = config;
            if (WindowHandle != handle)
            {
                WindowHandle = handle;
                HandleChanged = true;
            }
        }

        public void ResetHandleChangedFlag() => HandleChanged = false;
    }

    private readonly record struct WindowReapplyInfo(string Id, string Kind, IntPtr Handle, WindowConfig WindowConfig);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        private const int CchDeviceName = 32;
        private const int CchFormName = 32;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchDeviceName)]
        public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchFormName)]
        public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    private const int EnumCurrentSettings = -1;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumDisplaySettingsEx(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode, uint dwFlags);

    private sealed class NullTelemetry : ITelemetry
    {
        public static ITelemetry Instance { get; } = new NullTelemetry();

        public void Error(string message, Exception? exception = null)
        {
        }

        public void Info(string message, Exception? exception = null)
        {
        }

        public void Warn(string message, Exception? exception = null)
        {
        }
    }
}
