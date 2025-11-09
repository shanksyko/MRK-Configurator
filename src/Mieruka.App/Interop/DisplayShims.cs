using System;
using System.Collections.Generic;
using System.Drawing;
using Mieruka.Core.Models;
using CoreDisplayService = Mieruka.Core.Services.DisplayService;
using CoreDisplayUtils = Mieruka.Core.Services.DisplayUtils;
using Drawing = System.Drawing;

namespace Mieruka.App.Interop;

internal static class DisplayService
{
    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        return CoreDisplayService.GetMonitors();
    }

    public static Drawing.Rectangle GetVirtualBounds()
    {
        var monitors = GetMonitors();
        if (monitors.Count == 0)
        {
            return Drawing.Rectangle.Empty;
        }

        var left = int.MaxValue;
        var top = int.MaxValue;
        var right = int.MinValue;
        var bottom = int.MinValue;
        var hasBounds = false;

        foreach (var monitor in monitors)
        {
            if (monitor is null)
            {
                continue;
            }

            var bounds = monitor.Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                bounds = monitor.WorkArea;
            }

            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                continue;
            }

            left = Math.Min(left, bounds.Left);
            top = Math.Min(top, bounds.Top);
            right = Math.Max(right, bounds.Right);
            bottom = Math.Max(bottom, bounds.Bottom);
            hasBounds = true;
        }

        return hasBounds ? Drawing.Rectangle.FromLTRB(left, top, right, bottom) : Drawing.Rectangle.Empty;
    }

    public static MonitorInfo? GetMonitorFromPoint(Drawing.Point point)
    {
        MonitorInfo? fallback = null;
        foreach (var monitor in GetMonitors())
        {
            if (monitor is null)
            {
                continue;
            }

            var bounds = monitor.Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                bounds = monitor.WorkArea;
            }

            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                continue;
            }

            fallback ??= monitor;

            if (bounds.Contains(point))
            {
                return monitor;
            }
        }

        return fallback;
    }
}

internal static class DisplayUtils
{
    public static Drawing.Rectangle ClampToWorkArea(Drawing.Rectangle windowBounds, Drawing.Rectangle workArea)
    {
        return CoreDisplayUtils.ClampToWorkArea(windowBounds, workArea);
    }

    public static Drawing.Rectangle ClampToBounds(Drawing.Rectangle windowBounds, Drawing.Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return windowBounds;
        }

        return CoreDisplayUtils.ClampToWorkArea(windowBounds, bounds);
    }

    public static Drawing.Rectangle ClampToVirtualBounds(Drawing.Rectangle windowBounds)
    {
        var virtualBounds = DisplayService.GetVirtualBounds();
        return virtualBounds.IsEmpty ? windowBounds : CoreDisplayUtils.ClampToWorkArea(windowBounds, virtualBounds);
    }
}
