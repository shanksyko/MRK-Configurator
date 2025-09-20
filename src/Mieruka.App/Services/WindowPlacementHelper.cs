using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using Mieruka.Core.Models;
using Mieruka.Core.Services;

namespace Mieruka.App.Services;

/// <summary>
/// Provides helpers to calculate window placement using monitor metadata.
/// </summary>
internal static class WindowPlacementHelper
{
    private const int EnumCurrentSettings = -1;

    /// <summary>
    /// Resolves the monitor that should be used for the supplied window configuration.
    /// </summary>
    /// <param name="displayService">Display service used to query live monitors.</param>
    /// <param name="monitors">Monitor snapshot available in the workspace.</param>
    /// <param name="window">Window configuration that should be inspected.</param>
    /// <returns>The monitor that should host the window.</returns>
    public static MonitorInfo ResolveMonitor(
        IDisplayService? displayService,
        IReadOnlyList<MonitorInfo> monitors,
        WindowConfig window)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        ArgumentNullException.ThrowIfNull(window);

        if (displayService is not null)
        {
            var monitor = displayService.FindBy(window.Monitor);
            if (monitor is not null)
            {
                return monitor;
            }
        }

        foreach (var monitor in monitors)
        {
            if (MonitorKeysEqual(monitor.Key, window.Monitor))
            {
                return monitor;
            }
        }

        if (monitors.Count > 0)
        {
            return monitors[0];
        }

        return new MonitorInfo();
    }

    /// <summary>
    /// Calculates the target bounds for the supplied window configuration.
    /// </summary>
    /// <param name="window">Window configuration to apply.</param>
    /// <param name="monitor">Monitor that should host the window.</param>
    /// <returns>Absolute screen coordinates used for the window placement.</returns>
    public static Rectangle ResolveBounds(WindowConfig window, MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(monitor);

        var monitorBounds = GetMonitorBounds(monitor);
        return CalculateBounds(window, monitor, monitorBounds);
    }

    /// <summary>
    /// Retrieves the bounds for a monitor using the operating system APIs when available.
    /// </summary>
    /// <param name="monitor">Monitor that should be inspected.</param>
    /// <returns>Absolute bounds of the monitor.</returns>
    public static Rectangle GetMonitorBounds(MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        if (!OperatingSystem.IsWindows())
        {
            return new Rectangle(0, 0, monitor.Width, monitor.Height);
        }

        if (!string.IsNullOrWhiteSpace(monitor.DeviceName) &&
            TryGetDeviceBounds(monitor.DeviceName, out var bounds))
        {
            return bounds;
        }

        if (!string.IsNullOrWhiteSpace(monitor.Key.DeviceId) &&
            TryGetDeviceBounds(monitor.Key.DeviceId, out bounds))
        {
            return bounds;
        }

        return new Rectangle(0, 0, monitor.Width, monitor.Height);
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

    private static bool MonitorKeysEqual(MonitorKey left, MonitorKey right)
    {
        return string.Equals(left.DeviceId, right.DeviceId, StringComparison.OrdinalIgnoreCase)
            && left.DisplayIndex == right.DisplayIndex
            && left.AdapterLuidHigh == right.AdapterLuidHigh
            && left.AdapterLuidLow == right.AdapterLuidLow
            && left.TargetId == right.TargetId;
    }

    private static Rectangle NormalizeBounds(Rectangle bounds, MonitorInfo monitor)
    {
        var width = bounds.Width <= 0 ? monitor.Width : bounds.Width;
        var height = bounds.Height <= 0 ? monitor.Height : bounds.Height;
        return new Rectangle(bounds.Left, bounds.Top, width, height);
    }

    private static int ScaleValue(int value, double scale)
        => (int)Math.Round(value * scale, MidpointRounding.AwayFromZero);

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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettingsEx(
        string lpszDeviceName,
        int iModeNum,
        ref DEVMODE lpDevMode,
        int dwFlags);

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
}
