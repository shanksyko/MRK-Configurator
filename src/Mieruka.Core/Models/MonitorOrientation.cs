namespace Mieruka.Core.Models;

/// <summary>
/// Represents the orientation of a physical monitor.
/// </summary>
public enum MonitorOrientation
{
    /// <summary>
    /// Orientation could not be determined.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Landscape orientation (default).
    /// </summary>
    Landscape = 1,

    /// <summary>
    /// Portrait orientation rotated 90 degrees clockwise.
    /// </summary>
    Portrait = 2,

    /// <summary>
    /// Landscape orientation rotated 180 degrees.
    /// </summary>
    LandscapeFlipped = 3,

    /// <summary>
    /// Portrait orientation rotated 270 degrees clockwise.
    /// </summary>
    PortraitFlipped = 4,
}
