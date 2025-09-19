using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Mieruka.Preview;

internal static class MonitorUtilities
{
    [SupportedOSPlatform("windows")]
    public static bool TryGetMonitorHandle(string deviceName, out IntPtr monitorHandle, out RECT bounds)
    {
        monitorHandle = IntPtr.Zero;
        bounds = default;

        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return false;
        }

        var callback = new MonitorEnumProc((hMonitor, _, ref RECT rect, _) =>
        {
            var info = new MONITORINFOEX
            {
                cbSize = Marshal.SizeOf<MONITORINFOEX>(),
            };

            if (!GetMonitorInfo(hMonitor, ref info))
            {
                return true;
            }

            if (!string.Equals(info.szDevice, deviceName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            monitorHandle = hMonitor;
            bounds = info.rcMonitor;
            return false;
        });

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return monitorHandle != IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;

        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);
}
