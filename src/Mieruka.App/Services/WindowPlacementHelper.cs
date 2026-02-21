using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Mieruka.Core.Interop;
using Mieruka.Core.Layouts;
using Mieruka.Core.Models;
using Mieruka.Core.Monitors;
using Mieruka.Core.Services;
using Serilog;
using Drawing = System.Drawing;

namespace Mieruka.App.Services;

/// <summary>
/// Provides helpers to calculate window placement using monitor metadata.
/// </summary>
internal static class WindowPlacementHelper
{
    private const int EnumCurrentSettings = -1;
    private const int ErrorInvalidWindowHandle = 1400;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private static readonly TimeSpan DefaultPlacementTimeout = TimeSpan.FromSeconds(5);
    private const int SwRestore = 9;
    private const byte VkMenu = 0x12;
    private const uint KeyeventfKeyup = 0x0002;
    private static readonly IntPtr HwndTopMost = new(-1);
    private static readonly IntPtr HwndNoTopMost = new(-2);
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const uint SwpNoActivate = 0x0010;
    private const int MinVisiblePixels = 32;
    private static readonly Drawing.Size DefaultFallbackSize = new(1280, 720);
    private static readonly TimeSpan ForegroundActivationCooldown = TimeSpan.FromSeconds(2);
    private static readonly object ForegroundActivationLock = new();
    private static IntPtr s_lastForegroundHandle;
    private static DateTime s_lastForegroundActivationUtc = DateTime.MinValue;
    private static readonly ILogger Logger = Log.ForContext(typeof(WindowPlacementHelper));

    /// <summary>
    /// Represents a rectangular region defined as percentages of a monitor surface.
    /// </summary>
    public readonly struct ZoneRect
    {
        public ZoneRect(double leftPercentage, double topPercentage, double widthPercentage, double heightPercentage, ZoneAnchor anchor)
        {
            LeftPercentage = leftPercentage;
            TopPercentage = topPercentage;
            WidthPercentage = widthPercentage;
            HeightPercentage = heightPercentage;
            Anchor = anchor;
        }

        public double LeftPercentage { get; }

        public double TopPercentage { get; }

        public double WidthPercentage { get; }

        public double HeightPercentage { get; }

        public ZoneAnchor Anchor { get; }

        public static ZoneRect Full => new(0d, 0d, 100d, 100d, ZoneAnchor.TopLeft);

        public static ZoneRect FromZone(ZonePreset.Zone zone)
        {
            ArgumentNullException.ThrowIfNull(zone);
            return new ZoneRect(zone.LeftPercentage, zone.TopPercentage, zone.WidthPercentage, zone.HeightPercentage, zone.Anchor);
        }
    }

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
            var fallback = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
            ForEvent("MonitorFallback")
                .Warning(
                    "Nenhum monitor correspondente foi encontrado para {@RequestedMonitor}; usando fallback {FallbackStableId}.",
                    window.Monitor,
                    ResolveStableId(fallback));
            return fallback;
        }

        return new MonitorInfo();
    }

    /// <summary>
    /// Calculates the target bounds for the supplied window configuration.
    /// </summary>
    /// <param name="window">Window configuration to apply.</param>
    /// <param name="monitor">Monitor that should host the window.</param>
    /// <returns>Absolute screen coordinates used for the window placement.</returns>
    public static Drawing.Rectangle ResolveBounds(WindowConfig window, MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(monitor);

        var monitorBounds = GetMonitorBounds(monitor);
        var desired = CalculateBounds(window, monitor, monitorBounds);
        return ValidateAndClamp(desired, monitor, useWorkArea: false);
    }

    public static Drawing.Rectangle ValidateAndClamp(Drawing.Rectangle desired, MonitorInfo? monitor, bool useWorkArea)
    {
        var original = desired;
        Drawing.Rectangle monitorBounds = Drawing.Rectangle.Empty;
        Drawing.Rectangle workArea = Drawing.Rectangle.Empty;

        if (monitor is not null)
        {
            if (!TryGetMonitorAreas(monitor, out monitorBounds, out workArea))
            {
                monitorBounds = monitor.Bounds;
                workArea = monitor.WorkArea;

                if ((monitorBounds.Width <= 0 || monitorBounds.Height <= 0) && monitor.Width > 0 && monitor.Height > 0)
                {
                    monitorBounds = new Drawing.Rectangle(monitorBounds.Left, monitorBounds.Top, monitor.Width, monitor.Height);
                }

                if (workArea.Width <= 0 || workArea.Height <= 0)
                {
                    workArea = monitorBounds;
                }
            }
        }

        Drawing.Rectangle usableArea = useWorkArea ? workArea : monitorBounds;

        if (usableArea.Width <= 0 || usableArea.Height <= 0)
        {
            try
            {
                var descriptor = new MonitorService().PrimaryOrFirst();
                monitorBounds = descriptor.Bounds;
                workArea = descriptor.WorkArea;

                if (monitorBounds.Width <= 0 || monitorBounds.Height <= 0)
                {
                    if (descriptor.Width > 0 && descriptor.Height > 0)
                    {
                        monitorBounds = new Drawing.Rectangle(
                            monitorBounds.Left,
                            monitorBounds.Top,
                            descriptor.Width,
                            descriptor.Height);
                    }
                    else
                    {
                        monitorBounds = new Drawing.Rectangle(0, 0, DefaultFallbackSize.Width, DefaultFallbackSize.Height);
                    }
                }

                if (workArea.Width <= 0 || workArea.Height <= 0)
                {
                    workArea = monitorBounds;
                }

                usableArea = useWorkArea ? workArea : monitorBounds;

                Logger.Warning(
                    "MonitorFallback: Using fallback monitor {DeviceName} (Primary={IsPrimary}).",
                    descriptor.DeviceName,
                    descriptor.IsPrimary);
            }
            catch (InvalidOperationException ex)
            {
                var fallback = new Drawing.Rectangle(0, 0, DefaultFallbackSize.Width, DefaultFallbackSize.Height);
                Logger.Warning(
                    ex,
                    "MonitorFallback: No monitors detected, using default bounds {FallbackBounds}.",
                    fallback);
                return fallback;
            }
        }

        if (usableArea.Width <= 0 || usableArea.Height <= 0)
        {
            var fallback = new Drawing.Rectangle(0, 0, DefaultFallbackSize.Width, DefaultFallbackSize.Height);
            Logger.Warning(
                "InvalidBoundsDetected: Monitor area invalid, using default bounds {FallbackBounds}.",
                fallback);
            return fallback;
        }

        var adjusted = original;
        if (adjusted.Width <= 0 || adjusted.Height <= 0)
        {
            Logger.Warning(
                "InvalidBoundsDetected: Desired bounds {DesiredBounds} invalid, substituting monitor area {MonitorArea}.",
                adjusted,
                usableArea);
            adjusted = usableArea;
        }

        var preClamp = adjusted;

        var width = preClamp.Width;
        if (width < MinVisiblePixels)
        {
            width = Math.Min(MinVisiblePixels, usableArea.Width);
        }
        width = Math.Clamp(width, 1, usableArea.Width);

        var height = preClamp.Height;
        if (height < MinVisiblePixels)
        {
            height = Math.Min(MinVisiblePixels, usableArea.Height);
        }
        height = Math.Clamp(height, 1, usableArea.Height);

        var x = preClamp.Left;
        var y = preClamp.Top;

        if (x < usableArea.Left)
        {
            x = usableArea.Left;
        }

        if (y < usableArea.Top)
        {
            y = usableArea.Top;
        }

        if (x + width > usableArea.Right)
        {
            x = Math.Max(usableArea.Left, usableArea.Right - width);
        }

        if (y + height > usableArea.Bottom)
        {
            y = Math.Max(usableArea.Top, usableArea.Bottom - height);
        }

        var clamped = new Drawing.Rectangle(x, y, width, height);
        if (clamped != preClamp)
        {
            Logger.Information(
                "PlacementClamped: Adjusted bounds from {OriginalBounds} to {ClampedBounds} within area {MonitorArea}.",
                preClamp,
                clamped,
                usableArea);
        }

        return clamped;
    }

    /// <summary>
    /// Retrieves the bounds for a monitor using the operating system APIs when available.
    /// </summary>
    /// <param name="monitor">Monitor that should be inspected.</param>
    /// <returns>Absolute bounds of the monitor.</returns>
    public static Drawing.Rectangle GetMonitorBounds(MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        var fallbackBounds = CreateFallbackBounds(monitor);

        if (!OperatingSystem.IsWindows())
        {
            return fallbackBounds;
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

        return fallbackBounds;
    }

    /// <summary>
    /// Resolves a stable identifier for the provided monitor.
    /// </summary>
    public static string ResolveStableId(MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        if (!string.IsNullOrWhiteSpace(monitor.StableId))
        {
            return monitor.StableId;
        }

        if (!string.IsNullOrWhiteSpace(monitor.Key.DeviceId))
        {
            return monitor.Key.DeviceId;
        }

        if (!string.IsNullOrWhiteSpace(monitor.DeviceName))
        {
            return monitor.DeviceName;
        }

        return monitor.Key.DisplayIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Searches for a monitor using a stable identifier.
    /// </summary>
    public static MonitorInfo? GetMonitorByStableId(IReadOnlyList<MonitorInfo> monitors, string? stableId)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        if (string.IsNullOrWhiteSpace(stableId))
        {
            return null;
        }

        foreach (var monitor in monitors)
        {
            if (string.Equals(ResolveStableId(monitor), stableId, StringComparison.OrdinalIgnoreCase))
            {
                return monitor;
            }
        }

        return null;
    }

    public static MonitorInfo ResolveTargetMonitor(
        AppConfig app,
        string? selectedMonitorStableId,
        IReadOnlyList<MonitorInfo> monitors,
        IDisplayService? displayService)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(monitors);

        var monitor = GetMonitorByStableId(monitors, NormalizeStableId(app.TargetMonitorStableId));
        if (monitor is not null)
        {
            return monitor;
        }

        monitor = GetMonitorByStableId(monitors, NormalizeStableId(selectedMonitorStableId));
        if (monitor is not null)
        {
            return monitor;
        }

        return ResolveMonitor(displayService, monitors, app.Window);
    }

    public static MonitorInfo ResolveTargetMonitor(
        SiteConfig site,
        string? selectedMonitorStableId,
        IReadOnlyList<MonitorInfo> monitors,
        IDisplayService? displayService)
    {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(monitors);

        var monitor = GetMonitorByStableId(monitors, NormalizeStableId(site.TargetMonitorStableId));
        if (monitor is not null)
        {
            return monitor;
        }

        monitor = GetMonitorByStableId(monitors, NormalizeStableId(selectedMonitorStableId));
        if (monitor is not null)
        {
            return monitor;
        }

        return ResolveMonitor(displayService, monitors, site.Window);
    }

    public static ZoneRect ResolveTargetZone(
        MonitorInfo monitor,
        string? zoneIdentifier,
        WindowConfig window,
        IEnumerable<ZonePreset>? presets)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(window);

        var collection = presets ?? Array.Empty<ZonePreset>();

        if (!string.IsNullOrWhiteSpace(zoneIdentifier) &&
            TryGetZoneRect(collection, zoneIdentifier, out var zone))
        {
            return zone;
        }

        return CreateZoneFromWindow(window, monitor);
    }

    /// <summary>
    /// Attempts to resolve a zone preset entry using a combined identifier (preset[:zone]).
    /// </summary>
    public static bool TryGetZoneRect(IEnumerable<ZonePreset> presets, string? target, out ZoneRect zoneRect)
    {
        zoneRect = ZoneRect.Full;

        if (presets is null || string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        var (presetId, zoneId) = ParseZoneKey(target);

        var preset = presets.FirstOrDefault(p => string.Equals(p.Id, presetId, StringComparison.OrdinalIgnoreCase));
        if (preset is null)
        {
            return false;
        }

        ZonePreset.Zone? zone = null;

        if (!string.IsNullOrEmpty(zoneId))
        {
            zone = preset.Zones.FirstOrDefault(z => string.Equals(z.Id, zoneId, StringComparison.OrdinalIgnoreCase));
        }
        else if (preset.Zones.Count == 1)
        {
            zone = preset.Zones[0];
        }

        if (zone is null)
        {
            return false;
        }

        zoneRect = ZoneRect.FromZone(zone);
        return true;
    }

    /// <summary>
    /// Converts an absolute window configuration into a percentage-based zone.
    /// </summary>
    public static ZoneRect CreateZoneFromWindow(WindowConfig window, MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(monitor);

        if (window.FullScreen)
        {
            return ZoneRect.Full;
        }

        var monitorBounds = GetMonitorBounds(monitor);
        var monitorWidth = Math.Max(1, monitorBounds.Width > 0 ? monitorBounds.Width : monitor.Width);
        var monitorHeight = Math.Max(1, monitorBounds.Height > 0 ? monitorBounds.Height : monitor.Height);

        var width = Math.Clamp(window.Width ?? monitorWidth, 1, monitorWidth);
        var height = Math.Clamp(window.Height ?? monitorHeight, 1, monitorHeight);
        var x = Math.Clamp(window.X ?? 0, 0, Math.Max(0, monitorWidth - width));
        var y = Math.Clamp(window.Y ?? 0, 0, Math.Max(0, monitorHeight - height));

        var leftPercent = x / (double)monitorWidth * 100d;
        var topPercent = y / (double)monitorHeight * 100d;
        var widthPercent = width / (double)monitorWidth * 100d;
        var heightPercent = height / (double)monitorHeight * 100d;

        return new ZoneRect(leftPercent, topPercent, widthPercent, heightPercent, ZoneAnchor.TopLeft);
    }

    /// <summary>
    /// Calculates device coordinates for the supplied zone.
    /// </summary>
    public static Drawing.Rectangle CalculateZoneBounds(MonitorInfo monitor, ZoneRect zone)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        if (!TryGetMonitorAreas(monitor, out var monitorBounds, out var workArea))
        {
            monitorBounds = GetMonitorBounds(monitor);
            workArea = monitorBounds;
            if (monitorBounds.Width <= 0 || monitorBounds.Height <= 0)
            {
                ForEvent("InvalidBoundsDetected")
                    .Warning(
                        "Limites inválidos {Bounds} reportados para monitor {MonitorId} durante o cálculo de zona.",
                        monitorBounds,
                        ResolveStableId(monitor));
            }
        }

        var target = CalculateZoneRectangle(zone, monitorBounds);
        return ValidateAndClamp(target, monitor, useWorkArea: true);
    }

    public static async Task ForcePlaceProcessWindowAsync(
        Process proc,
        MonitorInfo mon,
        ZoneRect zone,
        bool topMost,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(proc);
        ArgumentNullException.ThrowIfNull(mon);

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        IntPtr handle;
        try
        {
            handle = await WaitForMainWindowAsync(proc, TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return;
        }

        if (!IsWindow(handle))
        {
            return;
        }

        ShowWindow(handle, SwRestore);
        await Task.Delay(50, ct).ConfigureAwait(false);

        var targetRect = ComputeTargetRect(mon, zone);

        var insertAfter = topMost ? HwndTopMost : HwndNoTopMost;
        SetWindowPos(handle, insertAfter, targetRect.X, targetRect.Y, targetRect.Width, targetRect.Height, SwpShowWindow | SwpNoOwnerZOrder | SwpNoActivate);

        if (!topMost)
        {
            TryBringToFront(handle);
        }
    }

    /// <summary>
    /// Positions a window handle inside the provided monitor zone.
    /// </summary>
    public static bool PlaceWindow(nint hWnd, MonitorInfo monitor, ZoneRect zone, bool topMost, TimeSpan? timeout = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        if (hWnd == 0)
        {
            return false;
        }

        var bounds = CalculateZoneBounds(monitor, zone);
        var (normalizedX, normalizedY, normalizedWidth, normalizedHeight) = NormalizeBounds(bounds);
        var normalizedBounds = new Drawing.Rectangle(normalizedX, normalizedY, normalizedWidth, normalizedHeight);
        var effectiveTimeout = timeout ?? DefaultPlacementTimeout;
        var stopwatch = Stopwatch.StartNew();
        var handle = (IntPtr)hWnd;

        while (stopwatch.Elapsed < effectiveTimeout)
        {
            if (!IsWindow(handle))
            {
                Thread.Sleep(50);
                continue;
            }

            try
            {
                WindowMover.MoveTo(handle, normalizedBounds, topMost, restoreIfMinimized: true);
                return true;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorInvalidWindowHandle)
            {
                Thread.Sleep(50);
            }
            catch (ArgumentException)
            {
                Thread.Sleep(50);
            }
        }

        return false;
    }

    /// <summary>
    /// Asynchronous version of <see cref="PlaceWindow"/> that avoids blocking the calling thread.
    /// </summary>
    public static async Task<bool> PlaceWindowAsync(nint hWnd, MonitorInfo monitor, ZoneRect zone, bool topMost, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        if (hWnd == 0)
        {
            return false;
        }

        var bounds = CalculateZoneBounds(monitor, zone);
        var (normalizedX, normalizedY, normalizedWidth, normalizedHeight) = NormalizeBounds(bounds);
        var normalizedBounds = new Drawing.Rectangle(normalizedX, normalizedY, normalizedWidth, normalizedHeight);
        var effectiveTimeout = timeout ?? DefaultPlacementTimeout;
        var stopwatch = Stopwatch.StartNew();
        var handle = (IntPtr)hWnd;

        while (stopwatch.Elapsed < effectiveTimeout)
        {
            ct.ThrowIfCancellationRequested();

            if (!IsWindow(handle))
            {
                await Task.Delay(50, ct).ConfigureAwait(false);
                continue;
            }

            try
            {
                WindowMover.MoveTo(handle, normalizedBounds, topMost, restoreIfMinimized: true);
                return true;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorInvalidWindowHandle)
            {
                await Task.Delay(50, ct).ConfigureAwait(false);
            }
            catch (ArgumentException)
            {
                await Task.Delay(50, ct).ConfigureAwait(false);
            }
        }

        return false;
    }

    private static Drawing.Rectangle CalculateBounds(WindowConfig window, MonitorInfo monitor, Drawing.Rectangle monitorBounds)
    {
        if (window.FullScreen)
        {
            return NormalizeBounds(monitorBounds, monitor);
        }

        var left = monitorBounds.Left;
        var top = monitorBounds.Top;

        if (window.X.HasValue)
        {
            left = monitorBounds.Left + window.X.Value;
        }

        if (window.Y.HasValue)
        {
            top = monitorBounds.Top + window.Y.Value;
        }

        var width = window.Width.HasValue
            ? window.Width.Value
            : monitorBounds.Width;

        var height = window.Height.HasValue
            ? window.Height.Value
            : monitorBounds.Height;

        if (width <= 0)
        {
            width = monitorBounds.Width;
        }

        if (height <= 0)
        {
            height = monitorBounds.Height;
        }

        return NormalizeBounds(new Drawing.Rectangle(left, top, width, height), monitor);
    }

    private static Drawing.Rectangle CalculateZoneRectangle(ZoneRect zone, Drawing.Rectangle monitorBounds)
    {
        var width = Math.Max(1, (int)Math.Round(monitorBounds.Width * (zone.WidthPercentage / 100d), MidpointRounding.AwayFromZero));
        var height = Math.Max(1, (int)Math.Round(monitorBounds.Height * (zone.HeightPercentage / 100d), MidpointRounding.AwayFromZero));
        var x = monitorBounds.Left + (int)Math.Round(monitorBounds.Width * (zone.LeftPercentage / 100d), MidpointRounding.AwayFromZero);
        var y = monitorBounds.Top + (int)Math.Round(monitorBounds.Height * (zone.TopPercentage / 100d), MidpointRounding.AwayFromZero);

        switch (zone.Anchor)
        {
            case ZoneAnchor.TopCenter:
                x -= width / 2;
                break;
            case ZoneAnchor.TopRight:
                x -= width;
                break;
            case ZoneAnchor.CenterLeft:
                y -= height / 2;
                break;
            case ZoneAnchor.Center:
                x -= width / 2;
                y -= height / 2;
                break;
            case ZoneAnchor.CenterRight:
                x -= width;
                y -= height / 2;
                break;
            case ZoneAnchor.BottomLeft:
                y -= height;
                break;
            case ZoneAnchor.BottomCenter:
                x -= width / 2;
                y -= height;
                break;
            case ZoneAnchor.BottomRight:
                x -= width;
                y -= height;
                break;
            case ZoneAnchor.TopLeft:
            default:
                break;
        }

        return new Drawing.Rectangle(x, y, width, height);
    }

    private static Drawing.Rectangle ComputeTargetRect(MonitorInfo monitor, ZoneRect zone)
    {
        if (!TryGetMonitorAreas(monitor, out var monitorBounds, out _))
        {
            monitorBounds = GetMonitorBounds(monitor);
        }

        var target = CalculateZoneRectangle(zone, monitorBounds);
        return ValidateAndClamp(target, monitor, useWorkArea: true);
    }

    private static async Task<IntPtr> WaitForMainWindowAsync(Process process, TimeSpan timeout, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (process.HasExited)
                {
                    break;
                }

                process.Refresh();
            }
            catch (InvalidOperationException)
            {
                break;
            }

            var handle = process.MainWindowHandle;
            if (handle != IntPtr.Zero && IsWindow(handle))
            {
                return handle;
            }

            await Task.Delay(50, ct).ConfigureAwait(false);
        }

        throw new TimeoutException("MainWindowHandle não apareceu.");
    }

    private static bool CanBringToFront(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        var utcNow = DateTime.UtcNow;

        lock (ForegroundActivationLock)
        {
            if (handle == s_lastForegroundHandle &&
                utcNow - s_lastForegroundActivationUtc < ForegroundActivationCooldown)
            {
                return false;
            }
        }

        return true;
    }

    private static void RecordForegroundActivation(IntPtr handle)
    {
        lock (ForegroundActivationLock)
        {
            s_lastForegroundHandle = handle;
            s_lastForegroundActivationUtc = DateTime.UtcNow;
        }
    }

    private static void TryBringToFront(IntPtr handle)
    {
        if (!OperatingSystem.IsWindows() || handle == IntPtr.Zero)
        {
            return;
        }

        if (handle == GetForegroundWindow())
        {
            return;
        }

        if (!CanBringToFront(handle))
        {
            return;
        }

        if (SetForegroundWindow(handle))
        {
            RecordForegroundActivation(handle);
            return;
        }

        keybd_event(VkMenu, 0, 0, UIntPtr.Zero);
        keybd_event(VkMenu, 0, KeyeventfKeyup, UIntPtr.Zero);
        SetForegroundWindow(handle);
        RecordForegroundActivation(handle);
    }

    private static bool TryGetMonitorAreas(MonitorInfo monitor, out Drawing.Rectangle bounds, out Drawing.Rectangle workArea)
    {
        bounds = Drawing.Rectangle.Empty;
        workArea = Drawing.Rectangle.Empty;

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var baseBounds = GetMonitorBounds(monitor);
        if (baseBounds.Width <= 0 || baseBounds.Height <= 0)
        {
            ForEvent("InvalidBoundsDetected")
                .Warning(
                    "Limites base inválidos {Bounds} ao obter áreas do monitor {MonitorId}.",
                    baseBounds,
                    ResolveStableId(monitor));
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

        bounds = Drawing.Rectangle.FromLTRB(info.rcMonitor.left, info.rcMonitor.top, info.rcMonitor.right, info.rcMonitor.bottom);
        workArea = Drawing.Rectangle.FromLTRB(info.rcWork.left, info.rcWork.top, info.rcWork.right, info.rcWork.bottom);
        return true;
    }

    private static (string PresetId, string? ZoneId) ParseZoneKey(string key)
    {
        var separatorIndex = key.IndexOf(':');
        if (separatorIndex < 0)
        {
            return (key.Trim(), null);
        }

        var presetId = key[..separatorIndex].Trim();
        var zoneIdPart = separatorIndex < key.Length - 1 ? key[(separatorIndex + 1)..] : string.Empty;
        var zoneId = string.IsNullOrWhiteSpace(zoneIdPart) ? null : zoneIdPart.Trim();
        return (presetId, zoneId);
    }

    private static bool MonitorKeysEqual(MonitorKey left, MonitorKey right)
    {
        return string.Equals(left.DeviceId, right.DeviceId, StringComparison.OrdinalIgnoreCase)
            && left.DisplayIndex == right.DisplayIndex
            && left.AdapterLuidHigh == right.AdapterLuidHigh
            && left.AdapterLuidLow == right.AdapterLuidLow
            && left.TargetId == right.TargetId;
    }

    private static Drawing.Rectangle NormalizeBounds(Drawing.Rectangle bounds, MonitorInfo monitor)
    {
        var monitorBounds = GetMonitorBounds(monitor);
        var fallbackWidth = monitorBounds.Width > 0 ? monitorBounds.Width : monitor.Width;
        var fallbackHeight = monitorBounds.Height > 0 ? monitorBounds.Height : monitor.Height;

        var width = bounds.Width <= 0 ? fallbackWidth : bounds.Width;
        var height = bounds.Height <= 0 ? fallbackHeight : bounds.Height;

        width = Math.Max(1, width);
        height = Math.Max(1, height);

        return new Drawing.Rectangle(bounds.Left, bounds.Top, width, height);
    }

    private static (int x, int y, int w, int h) NormalizeBounds(Drawing.Rectangle bounds)
    {
        var width = Math.Max(1, bounds.Width);
        var height = Math.Max(1, bounds.Height);
        return (bounds.X, bounds.Y, width, height);
    }

    private static Drawing.Rectangle CreateFallbackBounds(MonitorInfo monitor)
    {
        var hasValidSize = monitor.Bounds.Width > 0 && monitor.Bounds.Height > 0;
        if (hasValidSize)
        {
            return monitor.Bounds;
        }

        var width = monitor.Bounds.Width > 0 ? monitor.Bounds.Width : monitor.Width;
        var height = monitor.Bounds.Height > 0 ? monitor.Bounds.Height : monitor.Height;

        if (width <= 0)
        {
            width = Math.Max(1, monitor.Width);
        }

        if (height <= 0)
        {
            height = Math.Max(1, monitor.Height);
        }

        if (width <= 0)
        {
            width = DefaultFallbackSize.Width;
        }

        if (height <= 0)
        {
            height = DefaultFallbackSize.Height;
        }

        var left = monitor.Bounds != Drawing.Rectangle.Empty ? monitor.Bounds.Left : 0;
        var top = monitor.Bounds != Drawing.Rectangle.Empty ? monitor.Bounds.Top : 0;

        return new Drawing.Rectangle(left, top, width, height);
    }

    private static int ClampToInt(uint value)
    {
        return value > int.MaxValue ? int.MaxValue : (int)value;
    }

    private static ushort ToUShort(int value)
    {
        return unchecked((ushort)value);
    }

    private static ushort ToUShort(short value)
    {
        return unchecked((ushort)value);
    }

    private static string? NormalizeStableId(string? stableId)
        => string.IsNullOrWhiteSpace(stableId) ? null : stableId.Trim();

    private static bool TryGetDeviceBounds(string deviceName, out Drawing.Rectangle bounds)
    {
        bounds = Drawing.Rectangle.Empty;

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var devMode = new DEVMODE
        {
            dmSize = ToUShort(Marshal.SizeOf<DEVMODE>()),
        };

        if (!EnumDisplaySettingsEx(deviceName, EnumCurrentSettings, ref devMode, 0))
        {
            return false;
        }

        var width = ClampToInt(devMode.dmPelsWidth);
        var height = ClampToInt(devMode.dmPelsHeight);
        bounds = new Drawing.Rectangle(devMode.dmPositionX, devMode.dmPositionY, width, height);
        return width > 0 && height > 0;
    }

    private static ILogger ForEvent(string eventId)
        => Logger.ForContext("EventId", eventId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettingsEx(
        string lpszDeviceName,
        int iModeNum,
        ref DEVMODE lpDevMode,
        int dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

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
    private struct POINT
    {
        public int X;
        public int Y;
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
