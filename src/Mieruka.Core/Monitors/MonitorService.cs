using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using Mieruka.Core.Models;
using Mieruka.Core.WinAPI;

namespace Mieruka.Core.Monitors;

public interface IMonitorService
{
    IReadOnlyList<MonitorDescriptor> GetAll();
}

public sealed class MonitorDescriptor
{
    public string DeviceName = string.Empty;
    public long AdapterLuidHi;
    public long AdapterLuidLo;
    public uint TargetId;
    public string FriendlyName = string.Empty;
    public int Width;
    public int Height;
    public int RefreshHz;
    public bool IsPrimary;
    public Rectangle Bounds = Rectangle.Empty;
    public Rectangle WorkArea = Rectangle.Empty;
    public MonitorOrientation Orientation = MonitorOrientation.Unknown;
    public int Rotation;
}

public sealed class MonitorService : IMonitorService
{
    private const int ErrorSuccess = 0;

    public IReadOnlyList<MonitorDescriptor> GetAll()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<MonitorDescriptor>();
        }

        try
        {
            var monitors = EnumerateDisplayConfig();
            if (monitors.Count > 0)
            {
                return monitors;
            }
        }
        catch
        {
            // Ignore enumeration failures and fallback to GDI APIs.
        }

        try
        {
            return EnumerateGdi();
        }
        catch
        {
            return Array.Empty<MonitorDescriptor>();
        }
    }

    private static List<MonitorDescriptor> EnumerateDisplayConfig()
    {
        var descriptors = new List<MonitorDescriptor>();

        var flags = QueryDisplayConfigFlags.QdcOnlyActivePaths | QueryDisplayConfigFlags.QdcVirtualModeAware;
        if (GetDisplayConfigBufferSizes(flags, out var pathCount, out var modeCount) != ErrorSuccess || pathCount == 0)
        {
            return descriptors;
        }

        var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
        var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

        if (QueryDisplayConfig(flags, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != ErrorSuccess)
        {
            return descriptors;
        }

        for (var index = 0; index < pathCount; index++)
        {
            var path = paths[index];
            var target = path.targetInfo;

            if (!target.targetAvailable)
            {
                continue;
            }

            if (!TryGetTargetDeviceName(target.adapterId, target.id, out var targetName))
            {
                continue;
            }

            if (!TryGetSourceDeviceName(path.sourceInfo, out var sourceName))
            {
                continue;
            }

            var width = 0;
            var height = 0;
            var bounds = Rectangle.Empty;
            var workArea = Rectangle.Empty;
            if (TryGetSourceMode(path, modes, out var sourceMode))
            {
                width = (int)sourceMode.width;
                height = (int)sourceMode.height;
                bounds = new Rectangle(sourceMode.position.x, sourceMode.position.y, width, height);
            }

            var descriptor = new MonitorDescriptor
            {
                AdapterLuidHi = target.adapterId.HighPart,
                AdapterLuidLo = target.adapterId.LowPart,
                TargetId = target.id,
                DeviceName = !string.IsNullOrWhiteSpace(sourceName.viewGdiDeviceName)
                    ? sourceName.viewGdiDeviceName
                    : targetName.targetDeviceName ?? string.Empty,
                FriendlyName = !string.IsNullOrWhiteSpace(targetName.targetFriendlyName)
                    ? targetName.targetFriendlyName
                    : targetName.targetDeviceName ?? sourceName.viewGdiDeviceName ?? string.Empty,
                Width = width,
                Height = height,
                RefreshHz = CalculateRefreshRate(target.refreshRate),
                IsPrimary = ResolveIsPrimary(sourceName.viewGdiDeviceName),
                Orientation = MonitorOrientation.Unknown,
                Rotation = 0,
                Bounds = bounds,
                WorkArea = workArea,
            };

            if (TryGetMonitorRectangles(descriptor.DeviceName, out var monitorBounds, out var monitorWorkArea))
            {
                descriptor.Bounds = monitorBounds;
                descriptor.WorkArea = monitorWorkArea;
                descriptor.Width = monitorBounds.Width;
                descriptor.Height = monitorBounds.Height;
            }
            else if (!bounds.IsEmpty)
            {
                descriptor.Bounds = bounds;
                descriptor.WorkArea = bounds;
            }

            var (descriptorOrientation, descriptorRotation) = MapOrientation(target.rotation);
            if (descriptorOrientation != MonitorOrientation.Unknown)
            {
                descriptor.Orientation = descriptorOrientation;
                descriptor.Rotation = descriptorRotation;
            }

            descriptors.Add(descriptor);
        }

        return descriptors;
    }

    private static bool TryGetSourceMode(
        in DISPLAYCONFIG_PATH_INFO path,
        DISPLAYCONFIG_MODE_INFO[] modes,
        out DISPLAYCONFIG_SOURCE_MODE mode)
    {
        if (modes.Length > 0 && path.sourceInfo.modeInfoIdx < modes.Length)
        {
            var candidate = modes[path.sourceInfo.modeInfoIdx];
            if (candidate.infoType == DISPLAYCONFIG_MODE_INFO_TYPE.Source)
            {
                mode = candidate.modeInfo.sourceMode;
                return true;
            }
        }

        foreach (var candidate in modes)
        {
            if (candidate.infoType == DISPLAYCONFIG_MODE_INFO_TYPE.Source &&
                candidate.id == path.sourceInfo.id &&
                candidate.adapterId.HighPart == path.sourceInfo.adapterId.HighPart &&
                candidate.adapterId.LowPart == path.sourceInfo.adapterId.LowPart)
            {
                mode = candidate.modeInfo.sourceMode;
                return true;
            }
        }

        mode = default;
        return false;
    }

    private static int CalculateRefreshRate(DISPLAYCONFIG_RATIONAL refreshRate)
    {
        if (refreshRate.Denominator == 0)
        {
            return 0;
        }

        return (int)Math.Round(refreshRate.Numerator / (double)refreshRate.Denominator);
    }

    private static (MonitorOrientation Orientation, int Rotation) MapOrientation(DISPLAYCONFIG_ROTATION rotation)
        => rotation switch
        {
            DISPLAYCONFIG_ROTATION.DISPLAYCONFIG_ROTATION_ROTATE90 => (MonitorOrientation.Portrait, 90),
            DISPLAYCONFIG_ROTATION.DISPLAYCONFIG_ROTATION_ROTATE180 => (MonitorOrientation.LandscapeFlipped, 180),
            DISPLAYCONFIG_ROTATION.DISPLAYCONFIG_ROTATION_ROTATE270 => (MonitorOrientation.PortraitFlipped, 270),
            DISPLAYCONFIG_ROTATION.DISPLAYCONFIG_ROTATION_IDENTITY => (MonitorOrientation.Landscape, 0),
            _ => (MonitorOrientation.Unknown, 0),
        };

    private static (MonitorOrientation Orientation, int Rotation) MapOrientation(int displayOrientation)
        => displayOrientation switch
        {
            1 => (MonitorOrientation.Portrait, 90),
            2 => (MonitorOrientation.LandscapeFlipped, 180),
            3 => (MonitorOrientation.PortraitFlipped, 270),
            0 => (MonitorOrientation.Landscape, 0),
            _ => (MonitorOrientation.Unknown, 0),
        };

    private static bool TryGetMonitorRectangles(string? deviceName, out Rectangle bounds, out Rectangle workArea)
    {
        bounds = Rectangle.Empty;
        workArea = Rectangle.Empty;

        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return false;
        }

        var located = false;
        var locatedBounds = Rectangle.Empty;
        var locatedWorkArea = Rectangle.Empty;
        var callback = new MonitorEnumProc((IntPtr hMonitor, IntPtr _, ref RECT clip, IntPtr __) =>
        {
            var info = MONITORINFOEX.Create();
            if (!GetMonitorInfo(hMonitor, ref info))
            {
                return true;
            }

            if (!string.Equals(info.szDevice, deviceName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            locatedBounds = ToRectangle(info.rcMonitor);
            locatedWorkArea = ToRectangle(info.rcWork);
            located = true;
            return false;
        });

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        GC.KeepAlive(callback);

        if (located)
        {
            bounds = locatedBounds;
            workArea = locatedWorkArea;
        }

        return located;
    }

    private static Rectangle ToRectangle(RECT rect)
        => Rectangle.FromLTRB(rect.left, rect.top, rect.right, rect.bottom);

    private static bool ResolveIsPrimary(string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return false;
        }

        for (uint index = 0; ; index++)
        {
            var device = CreateDisplayDevice();
            if (!EnumDisplayDevices(null, index, ref device, 0))
            {
                break;
            }

            if (string.Equals(device.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
            {
                return device.StateFlags.HasFlag(DISPLAY_DEVICE_STATE_FLAGS.DISPLAY_DEVICE_PRIMARY_DEVICE);
            }
        }

        return false;
    }

    private static bool TryGetTargetDeviceName(
        LUID adapterId,
        uint targetId,
        out DISPLAYCONFIG_TARGET_DEVICE_NAME targetName)
    {
        targetName = new DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                adapterId = adapterId,
                id = targetId,
                type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
            },
        };

        return DisplayConfigGetDeviceInfo(ref targetName) == ErrorSuccess;
    }

    private static bool TryGetSourceDeviceName(
        DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo,
        out DISPLAYCONFIG_SOURCE_DEVICE_NAME sourceName)
    {
        sourceName = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                adapterId = sourceInfo.adapterId,
                id = sourceInfo.id,
                type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
            },
        };

        return DisplayConfigGetDeviceInfo(ref sourceName) == ErrorSuccess;
    }

    private static List<MonitorDescriptor> EnumerateGdi()
    {
        var descriptors = new List<MonitorDescriptor>();

        var callback = new MonitorEnumProc((IntPtr handle, IntPtr _, ref RECT clip, IntPtr __) =>
        {
            var info = MONITORINFOEX.Create();
            if (!GetMonitorInfo(handle, ref info))
            {
                return true;
            }

            var deviceName = info.szDevice ?? string.Empty;
            var bounds = ToRectangle(info.rcMonitor);
            var workArea = ToRectangle(info.rcWork);
            var refresh = 0;
            var orientation = MonitorOrientation.Unknown;
            var rotation = 0;
            if (TryGetDisplaySettings(deviceName, out var hz, out var orient, out var rot))
            {
                refresh = hz;
                orientation = orient;
                rotation = rot;
            }

            descriptors.Add(new MonitorDescriptor
            {
                DeviceName = deviceName,
                FriendlyName = ResolveFriendlyName(deviceName),
                Width = bounds.Width,
                Height = bounds.Height,
                RefreshHz = refresh,
                IsPrimary = (info.dwFlags & MonitorInfoFlags.Primary) != 0,
                Bounds = bounds,
                WorkArea = workArea,
                Orientation = orientation,
                Rotation = rotation,
            });

            return true;
        });

        return EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero)
            ? descriptors
            : new List<MonitorDescriptor>();
    }

    private static string ResolveFriendlyName(string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return string.Empty;
        }

        var monitor = CreateDisplayDevice();
        if (EnumDisplayDevices(deviceName, 0, ref monitor, 0))
        {
            return monitor.DeviceString ?? string.Empty;
        }

        return string.Empty;
    }

    private static bool TryGetDisplaySettings(
        string? deviceName,
        out int refreshRate,
        out MonitorOrientation orientation,
        out int rotation)
    {
        refreshRate = 0;
        orientation = MonitorOrientation.Unknown;
        rotation = 0;

        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return false;
        }

        try
        {
            var mode = new DEVMODE
            {
                dmSize = (short)Marshal.SizeOf<DEVMODE>(),
            };

            if (!EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref mode))
            {
                return false;
            }

            refreshRate = mode.dmDisplayFrequency;
            (orientation, rotation) = MapOrientation(mode.dmDisplayOrientation);
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static DISPLAY_DEVICE CreateDisplayDevice()
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

    private const int ENUM_CURRENT_SETTINGS = -1;

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, ref RECT clip, IntPtr data);

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(
        QueryDisplayConfigFlags flags,
        out uint numPathArrayElements,
        out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        QueryDisplayConfigFlags flags,
        ref uint numPathArrayElements,
        [Out] DISPLAYCONFIG_PATH_INFO[] pathInfoArray,
        ref uint numModeInfoArrayElements,
        [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME deviceName);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME deviceName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(
        string? lpDevice,
        uint iDevNum,
        ref DISPLAY_DEVICE lpDisplayDevice,
        uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc,
        IntPtr lprcClip,
        MonitorEnumProc lpfnEnum,
        IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY outputTechnology;
        public DISPLAYCONFIG_ROTATION rotation;
        public DISPLAYCONFIG_SCALING scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate;
        public DISPLAYCONFIG_SCANLINE_ORDERING scanLineOrdering;
        [MarshalAs(UnmanagedType.Bool)]
        public bool targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_MODE_INFO
    {
        public DISPLAYCONFIG_MODE_INFO_TYPE infoType;
        public uint id;
        public LUID adapterId;
        public DISPLAYCONFIG_MODE_INFO_UNION modeInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct DISPLAYCONFIG_MODE_INFO_UNION
    {
        [FieldOffset(0)]
        public DISPLAYCONFIG_TARGET_MODE targetMode;

        [FieldOffset(0)]
        public DISPLAYCONFIG_SOURCE_MODE sourceMode;

        [FieldOffset(0)]
        public DISPLAYCONFIG_DESKTOP_IMAGE_INFO desktopImageInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_SOURCE_MODE
    {
        public uint width;
        public uint height;
        public DISPLAYCONFIG_PIXEL_FORMAT pixelFormat;
        public POINTL position;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_TARGET_MODE
    {
        public DISPLAYCONFIG_VIDEO_SIGNAL_INFO targetVideoSignalInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO
    {
        public ulong pixelRate;
        public DISPLAYCONFIG_RATIONAL hSyncFreq;
        public DISPLAYCONFIG_RATIONAL vSyncFreq;
        public DISPLAYCONFIG_2DREGION activeSize;
        public DISPLAYCONFIG_2DREGION totalSize;
        public uint videoStandard;
        public DISPLAYCONFIG_SCANLINE_ORDERING scanLineOrdering;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_RATIONAL
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_2DREGION
    {
        public uint cx;
        public uint cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DESKTOP_IMAGE_INFO
    {
        public POINTL PathSourceSize;
        public POINTL PathSourceOffset;
        public POINTL DesktopImageClipSize;
        public POINTL DesktopImageClipOffset;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINTL
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public int LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public DISPLAYCONFIG_TARGET_DEVICE_NAME_FLAGS flags;
        public DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY outputTechnology;
        public DISPLAYCONFIG_ROTATION rotation;
        public DISPLAYCONFIG_SCALING scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate;
        public DISPLAYCONFIG_SCANLINE_ORDERING scanLineOrdering;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string targetFriendlyName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string targetDeviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_TARGET_DEVICE_NAME_FLAGS
    {
        public uint value;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public DISPLAYCONFIG_DEVICE_INFO_TYPE type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    private enum DISPLAYCONFIG_DEVICE_INFO_TYPE
    {
        DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1,
        DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2,
    }

    private enum DISPLAYCONFIG_MODE_INFO_TYPE
    {
        Source = 1,
        Target = 2,
        DesktopImage = 3,
    }

    private enum DISPLAYCONFIG_PIXEL_FORMAT
    {
        DISPLAYCONFIG_PIXEL_FORMAT_8BPP = 1,
        DISPLAYCONFIG_PIXEL_FORMAT_16BPP = 2,
        DISPLAYCONFIG_PIXEL_FORMAT_24BPP = 3,
        DISPLAYCONFIG_PIXEL_FORMAT_32BPP = 4,
        DISPLAYCONFIG_PIXEL_FORMAT_NONGDI = 5,
        DISPLAYCONFIG_PIXEL_FORMAT_16BPP565 = 6,
        DISPLAYCONFIG_PIXEL_FORMAT_16BPP555 = 7,
        DISPLAYCONFIG_PIXEL_FORMAT_32BPP101010 = 8,
        DISPLAYCONFIG_PIXEL_FORMAT_32BPP101010_P010 = 9,
    }

    private enum DISPLAYCONFIG_ROTATION
    {
        DISPLAYCONFIG_ROTATION_IDENTITY = 1,
        DISPLAYCONFIG_ROTATION_ROTATE90 = 2,
        DISPLAYCONFIG_ROTATION_ROTATE180 = 3,
        DISPLAYCONFIG_ROTATION_ROTATE270 = 4,
    }

    private enum DISPLAYCONFIG_SCALING
    {
        DISPLAYCONFIG_SCALING_IDENTITY = 1,
        DISPLAYCONFIG_SCALING_CENTERED = 2,
        DISPLAYCONFIG_SCALING_STRETCHED = 3,
        DISPLAYCONFIG_SCALING_ASPECTRATIOCENTEREDMAX = 4,
        DISPLAYCONFIG_SCALING_CUSTOM = 5,
        DISPLAYCONFIG_SCALING_PREFERRED = 128,
    }

    private enum DISPLAYCONFIG_SCANLINE_ORDERING
    {
        DISPLAYCONFIG_SCANLINE_ORDERING_UNSPECIFIED = 0,
        DISPLAYCONFIG_SCANLINE_ORDERING_PROGRESSIVE = 1,
        DISPLAYCONFIG_SCANLINE_ORDERING_INTERLACED = 2,
        DISPLAYCONFIG_SCANLINE_ORDERING_INTERLACED_UPPERFIELDFIRST = 3,
        DISPLAYCONFIG_SCANLINE_ORDERING_INTERLACED_LOWERFIELDFIRST = 4,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAY_DEVICE
    {
        private const int CchDeviceName = 32;
        private const int CchDeviceString = 128;

        public int cb;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchDeviceName)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchDeviceString)]
        public string DeviceString;

        public DISPLAY_DEVICE_STATE_FLAGS StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchDeviceString)]
        public string DeviceID;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchDeviceString)]
        public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        private const int CchDeviceName = 32;

        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public MonitorInfoFlags dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchDeviceName)]
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

    [Flags]
    private enum DISPLAY_DEVICE_STATE_FLAGS
    {
        DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x1,
        DISPLAY_DEVICE_MULTI_DRIVER = 0x2,
        DISPLAY_DEVICE_PRIMARY_DEVICE = 0x4,
    }

    [Flags]
    private enum MonitorInfoFlags
    {
        Primary = 0x00000001,
    }

    [Flags]
    private enum QueryDisplayConfigFlags : uint
    {
        QdcAllPaths = 0x00000001,
        QdcOnlyActivePaths = 0x00000002,
        QdcDatabaseCurrent = 0x00000004,
        QdcVirtualModeAware = 0x00000010,
    }
}
