using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mieruka.Core.Models;

namespace Mieruka.Core.Services;

/// <summary>
/// Detects monitors using classic GDI APIs.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GdiMonitorEnumerator
{
    private const int ErrorSuccess = 0;

    /// <summary>
    /// Enumerates the monitors that are currently active.
    /// </summary>
    public IReadOnlyList<MonitorProbe> Enumerate()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<MonitorProbe>();
        }

        var probes = new List<MonitorProbe>();
        var context = new GdiEnumContext(probes);
        var handle = GCHandle.Alloc(context);

        try
        {
            var callback = new MonitorEnumProc(EnumMonitorsCallback);
            if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, GCHandle.ToIntPtr(handle)))
            {
                return Array.Empty<MonitorProbe>();
            }

            return new ReadOnlyCollection<MonitorProbe>(context.Probes);
        }
        finally
        {
            if (handle.IsAllocated)
            {
                handle.Free();
            }
        }
    }

    private static bool EnumMonitorsCallback(IntPtr monitor, IntPtr hdc, ref RECT clip, IntPtr data)
    {
        var handle = GCHandle.FromIntPtr(data);
        var boxedContext = handle.Target;
        Debug.Assert(boxedContext is GdiEnumContext);

        if (boxedContext is not GdiEnumContext context)
        {
            return false;
        }

        var info = MONITORINFOEX.Create();
        if (!GetMonitorInfo(monitor, ref info))
        {
            return true;
        }

        var bounds = Rectangle.FromLTRB(info.rcMonitor.left, info.rcMonitor.top, info.rcMonitor.right, info.rcMonitor.bottom);
        var deviceName = info.szDevice ?? string.Empty;
        var friendlyName = ResolveFriendlyName(deviceName);
        var index = context.Probes.Count;
        var scale = TryGetScale(monitor);

        context.Probes.Add(new MonitorProbe
        {
            DeviceName = deviceName,
            FriendlyName = friendlyName,
            DisplayIndex = index,
            Bounds = bounds,
            IsPrimary = (info.dwFlags & MonitorInfoFlags.Primary) != 0,
            Scale = scale,
        });

        return true;
    }

    private static string ResolveFriendlyName(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return string.Empty;
        }

        var displayDevice = DISPLAY_DEVICE.Create();
        return EnumDisplayDevices(deviceName, 0, ref displayDevice, 0)
            ? displayDevice.DeviceString ?? string.Empty
            : string.Empty;
    }

    private static double TryGetScale(IntPtr monitorHandle)
    {
        try
        {
            if (GetDpiForMonitor(monitorHandle, MonitorDpiType.EffectiveDpi, out var dpiX, out _) != ErrorSuccess)
            {
                return 1.0;
            }

            return Math.Round(dpiX / 96.0, 2);
        }
        catch (DllNotFoundException)
        {
            return 1.0;
        }
        catch (EntryPointNotFoundException)
        {
            return 1.0;
        }
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, ref RECT clip, IntPtr data);

    private struct GdiEnumContext
    {
        public GdiEnumContext(List<MonitorProbe> probes)
        {
            Probes = probes;
        }

        public List<MonitorProbe> Probes { get; }
    }

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, MonitorDpiType dpiType, out uint dpiX, out uint dpiY);

    private enum MonitorInfoFlags
    {
        Primary = 0x00000001,
    }

    private enum MonitorDpiType
    {
        EffectiveDpi = 0,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        private const int CCHDEVICENAME = 32;

        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public MonitorInfoFlags dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        private const int CCHDEVICENAME = 32;
        private const int CCHDEVICESTRING = 128;

        public int cb;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICESTRING)]
        public string DeviceString;

        public DisplayDeviceStateFlags StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
        public string DeviceID;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICESTRING)]
        public string DeviceKey;

        public static DISPLAY_DEVICE Create()
        {
            return new DISPLAY_DEVICE
            {
                cb = Marshal.SizeOf<DISPLAY_DEVICE>(),
                DeviceName = string.Empty,
                DeviceString = string.Empty,
                DeviceID = string.Empty,
                DeviceKey = string.Empty,
            };
        }
    }

    [Flags]
    private enum DisplayDeviceStateFlags : uint
    {
        PrimaryDevice = 0x00000004,
    }
}
