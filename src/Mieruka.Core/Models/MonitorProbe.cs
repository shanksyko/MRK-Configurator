using System.Drawing;

namespace Mieruka.Core.Models;

/// <summary>
/// Lightweight monitor description produced by the GDI enumerator.
/// </summary>
public sealed record class MonitorProbe
{
    /// <summary>
    /// Logical device name reported by GDI (for example, <c>\\\\.\\DISPLAY1</c>).
    /// </summary>
    public string DeviceName { get; init; } = string.Empty;

    /// <summary>
    /// Friendly name reported by the display adapter, when available.
    /// </summary>
    public string FriendlyName { get; init; } = string.Empty;

    /// <summary>
    /// Zero-based display index used to keep a deterministic ordering.
    /// </summary>
    public int DisplayIndex { get; init; }

    /// <summary>
    /// Bounds of the monitor in virtual screen coordinates.
    /// </summary>
    public Rectangle Bounds { get; init; }

    /// <summary>
    /// Indicates whether the monitor is configured as the primary display.
    /// </summary>
    public bool IsPrimary { get; init; }

    /// <summary>
    /// Effective DPI scale factor reported by the operating system.
    /// </summary>
    public double Scale { get; init; } = 1.0;
}
