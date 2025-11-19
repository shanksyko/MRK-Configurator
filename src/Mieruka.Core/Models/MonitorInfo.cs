using System.Drawing;

namespace Mieruka.Core.Models;

/// <summary>
/// Describes a monitor that can be targeted by the configurator.
/// </summary>
public sealed record class MonitorInfo
{
    /// <summary>
    /// Unique key of the monitor.
    /// </summary>
    public MonitorKey Key { get; init; } = new();

    /// <summary>
    /// Friendly name displayed to the user.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Logical device name (for example, <c>\\\\.\\DISPLAY1</c>).
    /// </summary>
    public string DeviceName { get; init; } = string.Empty;

    /// <summary>
    /// Monitor width in pixels.
    /// </summary>
    public int Width { get; init; }

    /// <summary>
    /// Monitor height in pixels.
    /// </summary>
    public int Height { get; init; }

    /// <summary>
    /// Physical bounds of the monitor in virtual screen coordinates.
    /// </summary>
    public Rectangle Bounds { get; init; }

    /// <summary>
    /// Work area of the monitor in virtual screen coordinates.
    /// </summary>
    public Rectangle WorkArea { get; init; }

    /// <summary>
    /// Display scaling factor used by the operating system.
    /// </summary>
    public double Scale { get; init; } = 1.0;

    /// <summary>
    /// Orientation of the monitor.
    /// </summary>
    public MonitorOrientation Orientation { get; init; } = MonitorOrientation.Unknown;

    /// <summary>
    /// Rotation applied to the monitor in degrees.
    /// </summary>
    public int Rotation { get; init; }

    /// <summary>
    /// Indicates if the monitor is the primary one.
    /// </summary>
    public bool IsPrimary { get; init; }

    /// <summary>
    /// Technology used by the monitor connector.
    /// </summary>
    public string Connector { get; init; } = string.Empty;

    /// <summary>
    /// Extended display identification data identifier associated with the monitor.
    /// </summary>
    public string Edid { get; init; } = string.Empty;

    /// <summary>
    /// Stable identifier that should remain consistent across sessions.
    /// </summary>
    public string StableId { get; init; } = string.Empty;

    /// <summary>
    /// Identificador único usado para cache de captura.
    /// </summary>
    public string Id => string.IsNullOrEmpty(StableId) ? DeviceName : StableId;

    /// <summary>
    /// Calculates the logical preview resolution associated with this monitor.
    /// </summary>
    public PreviewResolution GetPreviewResolution()
    {
        return PreviewResolution.FromMonitor(this);
    }
}
