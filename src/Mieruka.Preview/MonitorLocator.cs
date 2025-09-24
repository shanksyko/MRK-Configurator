using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Mieruka.Core.Models;
using Mieruka.Core.Services;

namespace Mieruka.Preview;

public static class MonitorLocator
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public static MonitorInfo? Find(string monitorId)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(monitorId))
        {
            return null;
        }

        MonitorInfo? monitor = null;

        try
        {
            using var service = new DisplayService();
            var snapshot = service.Monitors();
            if (snapshot.Count > 0)
            {
                monitor = FindByIdentifier(snapshot, monitorId);
                if (monitor is not null)
                {
                    return monitor;
                }
            }
        }
        catch
        {
            // Fallback to GDI enumeration when the DisplayConfig service is unavailable.
        }

        return CreateFromScreens(monitorId);
    }

    private static MonitorInfo? FindByIdentifier(IReadOnlyList<MonitorInfo> monitors, string monitorId)
    {
        if (!MonitorIdentifier.TryParse(monitorId, out var key, out var deviceName))
        {
            deviceName = monitorId;
        }

        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            var matchByDevice = monitors.FirstOrDefault(m =>
                Comparer.Equals(m.DeviceName, deviceName) ||
                Comparer.Equals(m.Key.DeviceId, deviceName));

            if (matchByDevice is not null)
            {
                return matchByDevice;
            }
        }

        if (key.AdapterLuidHigh != 0 || key.AdapterLuidLow != 0 || key.TargetId != 0)
        {
            var matchByKey = monitors.FirstOrDefault(m =>
                m.Key.AdapterLuidHigh == key.AdapterLuidHigh &&
                m.Key.AdapterLuidLow == key.AdapterLuidLow &&
                m.Key.TargetId == key.TargetId);

            if (matchByKey is not null)
            {
                return matchByKey;
            }
        }

        return null;
    }

    private static MonitorInfo? CreateFromScreens(string monitorId)
    {
        var screens = Screen.AllScreens;
        if (screens is null || screens.Length == 0)
        {
            return null;
        }

        Screen? screen = null;
        foreach (var candidate in screens)
        {
            if (Comparer.Equals(candidate.DeviceName, monitorId))
            {
                screen = candidate;
                break;
            }
        }

        if (screen is null)
        {
            return null;
        }

        var index = Array.IndexOf(screens, screen);
        return new MonitorInfo
        {
            Key = new MonitorKey
            {
                DisplayIndex = index,
                DeviceId = screen.DeviceName ?? string.Empty,
            },
            Name = screen.DeviceName ?? string.Empty,
            DeviceName = screen.DeviceName ?? string.Empty,
            Width = screen.Bounds.Width,
            Height = screen.Bounds.Height,
            IsPrimary = screen.Primary,
            Scale = 1.0,
            Bounds = screen.Bounds,
            WorkArea = screen.WorkingArea,
            Orientation = MonitorOrientation.Unknown,
            Rotation = 0,
        };
    }
}
