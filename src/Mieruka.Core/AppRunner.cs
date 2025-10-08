using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Mieruka.Core.Interop;
using Mieruka.Core.Models;

namespace Mieruka.Core;

/// <summary>
/// Launches native applications and positions their windows on the selected monitor.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AppRunner
{
    private static readonly TimeSpan DefaultWindowTimeout = TimeSpan.FromSeconds(30);

    private readonly TimeSpan _windowTimeout;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppRunner"/> class.
    /// </summary>
    /// <param name="windowTimeout">Optional timeout applied while waiting for the main window handle.</param>
    public AppRunner(TimeSpan? windowTimeout = null)
    {
        _windowTimeout = windowTimeout ?? DefaultWindowTimeout;
    }

    /// <summary>
    /// Starts the supplied process and positions the resulting window on the selected monitor.
    /// </summary>
    /// <param name="startInfo">Information used to start the process.</param>
    /// <param name="monitor">Monitor that should host the window.</param>
    /// <param name="window">Window configuration describing the desired bounds.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <param name="onStarted">Callback invoked immediately after the process is started.</param>
    /// <returns><c>true</c> when the window was positioned successfully; otherwise, <c>false</c>.</returns>
    public async Task<bool> RunAndPositionAsync(
        ProcessStartInfo startInfo,
        MonitorInfo monitor,
        WindowConfig window,
        CancellationToken cancellationToken,
        Action<Process>? onStarted = null)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(monitor);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Application execution is only supported on Windows.");
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to launch process.");
        onStarted?.Invoke(process);

        var handle = await WaitForMainWindowAsync(process, cancellationToken).ConfigureAwait(false);
        handle = await RetryFindUncloakedAsync(process, handle, cancellationToken).ConfigureAwait(false);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        var bounds = ResolveBounds(window, monitor);
        cancellationToken.ThrowIfCancellationRequested();
        WindowMover.MoveTo(handle, bounds, window.AlwaysOnTop, restoreIfMinimized: true);
        return true;
    }

    /// <summary>
    /// Waits for the main window of the supplied process using the configured timeout.
    /// </summary>
    public Task<IntPtr> WaitForMainWindowAsync(Process process, CancellationToken cancellationToken)
        => WaitForMainWindowAsync(process, _windowTimeout, cancellationToken);

    /// <summary>
    /// Waits for the main window of the supplied process.
    /// </summary>
    public static async Task<IntPtr> WaitForMainWindowAsync(Process process, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Waiting for windows is only supported on Windows.");
        }

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        process.Refresh();
        var handle = process.MainWindowHandle;
        if (handle != IntPtr.Zero)
        {
            return handle;
        }

        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (process.HasExited)
            {
                throw new InvalidOperationException("The process exited before the main window was ready.");
            }

            await Task.Delay(150, cancellationToken).ConfigureAwait(false);

            process.Refresh();
            handle = process.MainWindowHandle;
            if (handle != IntPtr.Zero)
            {
                return handle;
            }
        }

        throw new TimeoutException("The main window was not detected within the timeout interval.");
    }

    /// <summary>
    /// Attempts to resolve an uncloaked window handle when the original window is cloaked.
    /// </summary>
    public Task<IntPtr> RetryFindUncloakedAsync(Process process, IntPtr handle, CancellationToken cancellationToken)
        => RetryFindUncloakedAsync(process, handle, _windowTimeout, cancellationToken);

    /// <summary>
    /// Attempts to resolve an uncloaked window handle when the original window is cloaked.
    /// </summary>
    public static async Task<IntPtr> RetryFindUncloakedAsync(Process process, IntPtr handle, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (!OperatingSystem.IsWindows())
        {
            return handle;
        }

        if (handle == IntPtr.Zero || !IsWindowCloaked(handle))
        {
            return handle;
        }

        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidate = FindUncloakedWindow(process.Id);
            if (candidate != IntPtr.Zero)
            {
                return candidate;
            }

            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }

        return handle;
    }

    /// <summary>
    /// Calculates the absolute bounds that should be applied to the window.
    /// </summary>
    public static Rectangle ResolveBounds(WindowConfig window, MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(monitor);

        var monitorBounds = GetMonitorBounds(monitor);
        if (monitorBounds.Width <= 0 || monitorBounds.Height <= 0)
        {
            monitorBounds = new Rectangle(0, 0, Math.Max(1, monitor.Width), Math.Max(1, monitor.Height));
        }

        if (window.FullScreen)
        {
            return monitorBounds;
        }

        var scale = monitor.Scale > 0 ? monitor.Scale : 1.0;
        var left = window.X.HasValue
            ? monitorBounds.Left + ScaleValue(window.X.Value, scale)
            : monitorBounds.Left;
        var top = window.Y.HasValue
            ? monitorBounds.Top + ScaleValue(window.Y.Value, scale)
            : monitorBounds.Top;
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

        var target = new Rectangle(left, top, width, height);

        if (TryGetMonitorAreas(monitor, out _, out var workArea))
        {
            target = ClampToWorkArea(target, workArea);
        }

        return target;
    }

    private static Rectangle ClampToWorkArea(Rectangle target, Rectangle workArea)
    {
        if (workArea.Width <= 0 || workArea.Height <= 0)
        {
            return target;
        }

        var width = Math.Min(target.Width, workArea.Width);
        var height = Math.Min(target.Height, workArea.Height);

        var maxLeft = Math.Max(workArea.Left, workArea.Right - width);
        var maxTop = Math.Max(workArea.Top, workArea.Bottom - height);

        var x = Math.Clamp(target.Left, workArea.Left, maxLeft);
        var y = Math.Clamp(target.Top, workArea.Top, maxTop);

        return new Rectangle(x, y, width, height);
    }

    private static Rectangle GetMonitorBounds(MonitorInfo monitor)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new Rectangle(0, 0, Math.Max(1, monitor.Width), Math.Max(1, monitor.Height));
        }

        if (!string.IsNullOrWhiteSpace(monitor.DeviceName) && TryGetDeviceBounds(monitor.DeviceName, out var bounds))
        {
            return bounds;
        }

        if (!string.IsNullOrWhiteSpace(monitor.Key.DeviceId) && TryGetDeviceBounds(monitor.Key.DeviceId, out bounds))
        {
            return bounds;
        }

        if (monitor.Bounds.Width > 0 && monitor.Bounds.Height > 0)
        {
            return monitor.Bounds;
        }

        return new Rectangle(0, 0, Math.Max(1, monitor.Width), Math.Max(1, monitor.Height));
    }

    private static bool TryGetMonitorAreas(MonitorInfo monitor, out Rectangle bounds, out Rectangle workArea)
    {
        bounds = Rectangle.Empty;
        workArea = Rectangle.Empty;

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var baseBounds = GetMonitorBounds(monitor);
        if (baseBounds.Width <= 0 || baseBounds.Height <= 0)
        {
            return false;
        }

        var point = new POINT
        {
            X = baseBounds.Left + (baseBounds.Width / 2),
            Y = baseBounds.Top + (baseBounds.Height / 2),
        };

        var handle = MonitorFromPoint(point, MonitorDefaultToNearest);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        var info = MONITORINFOEX.Create();
        if (!GetMonitorInfo(handle, ref info))
        {
            return false;
        }

        bounds = Rectangle.FromLTRB(info.rcMonitor.left, info.rcMonitor.top, info.rcMonitor.right, info.rcMonitor.bottom);
        workArea = Rectangle.FromLTRB(info.rcWork.left, info.rcWork.top, info.rcWork.right, info.rcWork.bottom);
        return true;
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
            dmSize = (ushort)Marshal.SizeOf<DEVMODE>(),
        };

        if (!EnumDisplaySettingsEx(deviceName, EnumCurrentSettings, ref devMode, 0))
        {
            return false;
        }

        var width = ClampToInt(devMode.dmPelsWidth);
        var height = ClampToInt(devMode.dmPelsHeight);
        bounds = new Rectangle(devMode.dmPositionX, devMode.dmPositionY, width, height);
        return width > 0 && height > 0;
    }

    private static int ScaleValue(int value, double scale)
        => (int)Math.Round(value * scale, MidpointRounding.AwayFromZero);

    private static int ClampToInt(uint value)
        => value > int.MaxValue ? int.MaxValue : (int)value;

    private static bool IsWindowCloaked(IntPtr handle)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var result = DwmGetWindowAttribute(handle, DWMWA_CLOAKED, out int cloaked, sizeof(int));
        return result == 0 && cloaked != DwmCloakedUncloaked;
    }

    private static IntPtr FindUncloakedWindow(int processId)
    {
        IntPtr result = IntPtr.Zero;

        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
            {
                return true;
            }

            _ = GetWindowThreadProcessId(hwnd, out var currentProcessId);
            if (currentProcessId != processId)
            {
                return true;
            }

            if (IsWindowCloaked(hwnd))
            {
                return true;
            }

            result = hwnd;
            return false;
        }, IntPtr.Zero);

        return result;
    }

    private delegate bool EnumWindowsCallback(IntPtr hWnd, IntPtr lParam);

    private const int EnumCurrentSettings = -1;
    private const int DWMWA_CLOAKED = 14;
    private const int DwmCloakedUncloaked = 0;
    private const uint MonitorDefaultToNearest = 0x00000002;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettingsEx(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode, int dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        private const int CchDeviceName = 32;
        private const int CchFormName = 32;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchDeviceName)]
        public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchFormName)]
        public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        private const int CchDevicename = 32;

        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchDevicename)]
        public string szDevice;

        public static MONITORINFOEX Create()
        {
            return new MONITORINFOEX
            {
                cbSize = (uint)Marshal.SizeOf<MONITORINFOEX>(),
                szDevice = string.Empty,
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }
}
