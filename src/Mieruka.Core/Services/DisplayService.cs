using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Mieruka.Core.Models;

namespace Mieruka.Core.Services;

/// <summary>
/// Enumerates the monitors available in the system and observes topology changes.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DisplayService : IDisplayService, IDisposable
{
    private const int ErrorSuccess = 0;

    private readonly object _gate = new();
    private readonly ITelemetry _telemetry;
    private readonly SessionChecker _sessionChecker;
    private readonly bool _ownsSessionChecker;
    private IReadOnlyList<MonitorInfo> _snapshot = Array.Empty<MonitorInfo>();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="DisplayService"/> class.
    /// </summary>
    /// <param name="telemetry">Telemetry sink used to record topology changes.</param>
    public DisplayService(ITelemetry? telemetry = null, SessionChecker? sessionChecker = null)
    {
        _telemetry = telemetry ?? NullTelemetry.Instance;
        _sessionChecker = sessionChecker ?? new SessionChecker(_telemetry);
        _ownsSessionChecker = sessionChecker is null;

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        _sessionChecker.UpdateStatus();

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;

        RefreshSnapshot();
    }

    /// <inheritdoc />
    public event EventHandler? TopologyChanged;

    /// <inheritdoc />
    public IReadOnlyList<MonitorInfo> Monitors()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<MonitorInfo>();
        }

        lock (_gate)
        {
            if (_snapshot.Count == 0)
            {
                _snapshot = EnumerateMonitors();
            }

            return _snapshot;
        }
    }

    /// <inheritdoc />
    public MonitorInfo? FindBy(MonitorKey key)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var comparer = StringComparer.OrdinalIgnoreCase;
        return Monitors().FirstOrDefault(m =>
            comparer.Equals(m.Key.DeviceId, key.DeviceId) &&
            m.Key.AdapterLuidHigh == key.AdapterLuidHigh &&
            m.Key.AdapterLuidLow == key.AdapterLuidLow &&
            m.Key.TargetId == key.TargetId);
    }

    /// <inheritdoc />
    public MonitorInfo? FindByDeviceName(string deviceName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var comparer = StringComparer.OrdinalIgnoreCase;
        return Monitors().FirstOrDefault(m =>
            comparer.Equals(m.DeviceName, deviceName) ||
            comparer.Equals(m.Key.DeviceId, deviceName));
    }

    /// <summary>
    /// Releases resources associated with the service.
    /// </summary>
    public void Dispose()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (_disposed)
        {
            return;
        }

        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;

        if (_ownsSessionChecker)
        {
            _sessionChecker.Dispose();
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        => NotifyTopologyChanged();

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode is PowerModes.StatusChange or PowerModes.Resume)
        {
            NotifyTopologyChanged();
        }
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        _sessionChecker.UpdateStatus();

        if (e.Reason is SessionSwitchReason.RemoteConnect or SessionSwitchReason.RemoteDisconnect or
            SessionSwitchReason.ConsoleConnect or SessionSwitchReason.ConsoleDisconnect)
        {
            NotifyTopologyChanged();
        }
    }

    private void NotifyTopologyChanged()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (_sessionChecker.UpdateStatus())
        {
            _telemetry.Info("topologia ignorada devido a sessão RDP desconectada");
            return;
        }

        RefreshSnapshot();
        _telemetry.Info("reposicionado por hot-plug");
        TopologyChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshSnapshot()
    {
        lock (_gate)
        {
            _snapshot = EnumerateMonitors();
        }
    }

    private static IReadOnlyList<MonitorInfo> EnumerateMonitors()
    {
        var monitors = new List<MonitorInfo>();

        var flags = QueryDisplayConfigFlags.QdcOnlyActivePaths | QueryDisplayConfigFlags.QdcVirtualModeAware;
        if (GetDisplayConfigBufferSizes(flags, out var pathCount, out var modeCount) != ErrorSuccess)
        {
            return monitors.AsReadOnly();
        }

        if (pathCount == 0)
        {
            return monitors.AsReadOnly();
        }

        var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
        var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

        var queryResult = QueryDisplayConfig(flags, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
        if (queryResult != ErrorSuccess)
        {
            return monitors.AsReadOnly();
        }

        for (var index = 0; index < pathCount; index++)
        {
            var path = paths[index];
            var target = path.targetInfo;

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
            var scale = 1.0;

            if (TryGetSourceMode(path, modes, out var sourceMode))
            {
                width = (int)sourceMode.width;
                height = (int)sourceMode.height;

                if (TryGetScale(sourceMode, out var calculatedScale))
                {
                    scale = calculatedScale;
                }
            }

            var adapterDevice = GetDisplayAdapter(sourceName.viewGdiDeviceName);
            var monitorDevice = GetMonitorDevice(sourceName.viewGdiDeviceName);

            var monitorInfo = new MonitorInfo
            {
                Key = new MonitorKey
                {
                    DeviceId = targetName.targetDeviceName ?? string.Empty,
                    DisplayIndex = index,
                    AdapterLuidHigh = target.adapterId.HighPart,
                    AdapterLuidLow = target.adapterId.LowPart,
                    TargetId = unchecked((int)target.id),
                },
                Name = targetName.targetFriendlyName ?? string.Empty,
                DeviceName = sourceName.viewGdiDeviceName ?? string.Empty,
                Width = width,
                Height = height,
                Scale = scale,
                IsPrimary = adapterDevice?.StateFlags.HasFlag(DISPLAY_DEVICE_STATE_FLAGS.DISPLAY_DEVICE_PRIMARY_DEVICE) ?? false,
                Connector = GetConnectorName(targetName.outputTechnology),
                Edid = ExtractEdid(monitorDevice),
            };

            monitors.Add(monitorInfo);
        }

        return new ReadOnlyCollection<MonitorInfo>(monitors);
    }

    private static bool TryGetSourceMode(in DISPLAYCONFIG_PATH_INFO path, DISPLAYCONFIG_MODE_INFO[] modeInfos, out DISPLAYCONFIG_SOURCE_MODE sourceMode)
    {
        if (modeInfos.Length > 0 && path.sourceInfo.modeInfoIdx < modeInfos.Length)
        {
            var candidate = modeInfos[path.sourceInfo.modeInfoIdx];
            if (candidate.infoType == DISPLAYCONFIG_MODE_INFO_TYPE.Source)
            {
                sourceMode = candidate.modeInfo.sourceMode;
                return true;
            }
        }

        foreach (var mode in modeInfos)
        {
            if (mode.infoType == DISPLAYCONFIG_MODE_INFO_TYPE.Source &&
                mode.id == path.sourceInfo.id &&
                mode.adapterId.HighPart == path.sourceInfo.adapterId.HighPart &&
                mode.adapterId.LowPart == path.sourceInfo.adapterId.LowPart)
            {
                sourceMode = mode.modeInfo.sourceMode;
                return true;
            }
        }

        sourceMode = default;
        return false;
    }

    private static bool TryGetScale(DISPLAYCONFIG_SOURCE_MODE sourceMode, out double scale)
    {
        scale = 1.0;

        var monitorPoint = new POINT
        {
            X = sourceMode.position.x + (int)(sourceMode.width / 2),
            Y = sourceMode.position.y + (int)(sourceMode.height / 2),
        };

        var monitor = MonitorFromPoint(monitorPoint, MonitorFromPointFlags.MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            if (GetDpiForMonitor(monitor, MonitorDpiType.EffectiveDpi, out var dpiX, out _) != ErrorSuccess)
            {
                return false;
            }

            scale = Math.Round(dpiX / 96.0, 2);
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

    private static bool TryGetTargetDeviceName(LUID adapterId, uint targetId, out DISPLAYCONFIG_TARGET_DEVICE_NAME targetName)
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

    private static bool TryGetSourceDeviceName(DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo, out DISPLAYCONFIG_SOURCE_DEVICE_NAME sourceName)
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

    private static DISPLAY_DEVICE? GetDisplayAdapter(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return null;
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
                return device;
            }
        }

        return null;
    }

    private static DISPLAY_DEVICE? GetMonitorDevice(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return null;
        }

        var monitor = CreateDisplayDevice();
        return EnumDisplayDevices(deviceName, 0, ref monitor, 0) ? monitor : null;
    }

    private static DISPLAY_DEVICE CreateDisplayDevice()
        => new() { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };

    private static string ExtractEdid(DISPLAY_DEVICE? monitorDevice)
    {
        if (monitorDevice is null)
        {
            return string.Empty;
        }

        var deviceId = monitorDevice.Value.DeviceID;
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return string.Empty;
        }

        var parts = deviceId.Split('\\');
        return parts.Length >= 2 ? parts[1] : deviceId;
    }

    private static string GetConnectorName(DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY technology)
        => technology switch
        {
            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HD15 => "VGA",
            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_SVIDEO => "S-Video",
            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_COMPOSITE_VIDEO => "Composite",
            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_COMPONENT_VIDEO => "Component",
            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DVI => "DVI",
            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HDMI => "HDMI",
            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_LVDS => "LVDS",
            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_D_JPN => "D-JPN",
            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_SDI => "SDI",
            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EXTERNAL => "DisplayPort",
            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED => "eDP",
            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EXTERNAL => "UDI",
            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED => "UDI Embedded",
            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_SDTVDONGLE => "SDTV Dongle",
            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_MIRACAST => "Miracast",
            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL => "Internal",
            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_USB_TUNNEL => "USB Type-C",
            _ => technology.ToString(),
        };

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
    private struct DISPLAYCONFIG_RATIONAL
    {
        public uint Numerator;
        public uint Denominator;
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
    private struct DISPLAY_DEVICE
    {
        public int cb;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public DISPLAY_DEVICE_STATE_FLAGS StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
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
        DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_PREFERRED_MODE = 3,
        DISPLAYCONFIG_DEVICE_INFO_GET_ADAPTER_NAME = 4,
        DISPLAYCONFIG_DEVICE_INFO_SET_TARGET_PERSISTENCE = 5,
        DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_BASE_TYPE = 6,
        DISPLAYCONFIG_DEVICE_INFO_GET_SUPPORT_VIRTUAL_RESOLUTION = 7,
        DISPLAYCONFIG_DEVICE_INFO_SET_SUPPORT_VIRTUAL_RESOLUTION = 8,
        DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO = 9,
        DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE = 10,
        DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL = 11,
        DISPLAYCONFIG_DEVICE_INFO_GET_MONITOR_SPECIALIZATION = 12,
        DISPLAYCONFIG_DEVICE_INFO_SET_MONITOR_SPECIALIZATION = 13,
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

    [Flags]
    private enum DISPLAY_DEVICE_STATE_FLAGS
    {
        DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x1,
        DISPLAY_DEVICE_MULTI_DRIVER = 0x2,
        DISPLAY_DEVICE_PRIMARY_DEVICE = 0x4,
        DISPLAY_DEVICE_MIRRORING_DRIVER = 0x8,
        DISPLAY_DEVICE_VGA_COMPATIBLE = 0x10,
        DISPLAY_DEVICE_REMOVABLE = 0x20,
        DISPLAY_DEVICE_MODESPRUNED = 0x8000000,
        DISPLAY_DEVICE_REMOTE = 0x4000000,
        DISPLAY_DEVICE_DISCONNECT = 0x2000000,
    }

    private enum QueryDisplayConfigFlags : uint
    {
        QdcAllPaths = 0x00000001,
        QdcOnlyActivePaths = 0x00000002,
        QdcDatabaseCurrent = 0x00000004,
        QdcVirtualModeAware = 0x00000010,
    }

    private enum MonitorDpiType
    {
        EffectiveDpi = 0,
        AngularDpi = 1,
        RawDpi = 2,
    }

    private enum MonitorFromPointFlags : uint
    {
        MONITOR_DEFAULTTONULL = 0x00000000,
        MONITOR_DEFAULTTOPRIMARY = 0x00000001,
        MONITOR_DEFAULTTONEAREST = 0x00000002,
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(QueryDisplayConfigFlags flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(QueryDisplayConfigFlags flags, ref uint numPathArrayElements, [Out] DISPLAYCONFIG_PATH_INFO[] pathInfoArray, ref uint numModeInfoArrayElements, [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME deviceName);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME deviceName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, MonitorFromPointFlags dwFlags);

    [DllImport("Shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, MonitorDpiType dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private enum DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY : uint
    {
        DISPLAYCONFIG_OUTPUT_TECHNOLOGY_OTHER = 0xFFFFFFFF,
        DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HD15 = 0,
        DISPLAYCONFIG_OUTPUT_TECHNOLOGY_SVIDEO = 1,
        DISPLAYCONFIG_OUTPUT_TECHNOLOGY_COMPOSITE_VIDEO = 2,
        DISPLAYCONFIG_OUTPUT_TECHNOLOGY_COMPONENT_VIDEO = 3,
        DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DVI = 4,
        DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HDMI = 5,
        DISPLAYCONFIG_OUTPUT_TECHNOLOGY_LVDS = 6,
        DISPLAYCONFIG_OUTPUT_TECHNOLOGY_D_JPN = 8,
        DISPLAYCONFIG_OUTPUT_TECHNOLOGY_SDI = 9,
        DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EXTERNAL = 10,
        DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED = 11,
        DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EXTERNAL = 12,
        DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED = 13,
        DISPLAYCONFIG_OUTPUT_TECHNOLOGY_SDTVDONGLE = 14,
        DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_USB_TUNNEL = 15,
        DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL = 0x80000000,
        DISPLAYCONFIG_OUTPUT_TECHNOLOGY_MIRACAST = 0x80000001,
    }
}
