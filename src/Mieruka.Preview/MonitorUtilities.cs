using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Serilog;

namespace Mieruka.Preview;

internal static class MonitorUtilities
{
    private static readonly ILogger Logger = Log.ForContext(typeof(MonitorUtilities));

    [SupportedOSPlatform("windows")]
    public static bool TryGetMonitorHandle(string deviceName, out IntPtr monitorHandle, out RECT bounds)
    {
        monitorHandle = IntPtr.Zero;
        bounds = default;

        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return false;
        }

        var context = new MonitorSearchContext
        {
            DeviceName = deviceName,
            Monitor = IntPtr.Zero,
            Bounds = default,
            Located = false,
        };

        var handle = GCHandle.Alloc(context);
        try
        {
            var callback = new MonitorEnumProc(EnumMonitorCallback);
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, GCHandle.ToIntPtr(handle));
            GC.KeepAlive(callback);

            if (handle.Target is not MonitorSearchContext finalContext)
            {
                ForEvent("InteropGuardTriggered")
                    .Warning("Contexto inesperado ao enumerar monitor '{DeviceName}'.", deviceName);
                return false;
            }

            if (!finalContext.Located)
            {
                return false;
            }

            monitorHandle = finalContext.Monitor;
            bounds = finalContext.Bounds;
            return true;
        }
        finally
        {
            if (handle.IsAllocated)
            {
                handle.Free();
            }
        }
    }

    private struct MonitorSearchContext
    {
        public string DeviceName;
        public IntPtr Monitor;
        public RECT Bounds;
        public bool Located;
    }

    private static bool EnumMonitorCallback(IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data)
    {
        if (data == IntPtr.Zero)
        {
            ForEvent("InteropGuardTriggered").Warning("Enumeração de monitores abortada: ponteiro de contexto estava vazio.");
            return false;
        }

        var handle = GCHandle.FromIntPtr(data);
        if (!handle.IsAllocated)
        {
            ForEvent("InteropGuardTriggered").Warning("Enumeração de monitores abortada: GCHandle não estava alocado.");
            return false;
        }

        if (handle.Target is not MonitorSearchContext context)
        {
            ForEvent("InteropGuardTriggered")
                .Warning("Enumeração de monitores abortada: tipo inesperado {TargetType}.", handle.Target?.GetType().FullName ?? "<null>");
            return false;
        }
        Debug.Assert(!string.IsNullOrEmpty(context.DeviceName));

        var info = new MONITORINFOEX
        {
            cbSize = Marshal.SizeOf<MONITORINFOEX>(),
        };

        if (!GetMonitorInfo(monitor, ref info))
        {
            return true;
        }

        if (!string.Equals(info.szDevice, context.DeviceName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        context.Monitor = monitor;
        context.Bounds = info.rcMonitor;
        context.Located = true;
        handle.Target = context;
        return false;
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

    private static ILogger ForEvent(string eventId)
        => Logger.ForContext("EventId", eventId);
}
