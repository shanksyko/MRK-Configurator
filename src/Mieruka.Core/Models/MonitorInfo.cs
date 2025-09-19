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
    /// Monitor width in pixels.
    /// </summary>
    public int Width { get; init; }

    /// <summary>
    /// Monitor height in pixels.
    /// </summary>
    public int Height { get; init; }

    /// <summary>
    /// Display scaling factor used by the operating system.
    /// </summary>
    public double Scale { get; init; } = 1.0;

    /// <summary>
    /// Indicates if the monitor is the primary one.
    /// </summary>
    public bool IsPrimary { get; init; }
}
