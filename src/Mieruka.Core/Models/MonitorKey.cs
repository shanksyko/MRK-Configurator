namespace Mieruka.Core.Models;

/// <summary>
/// Identifies a monitor in the system.
/// </summary>
public sealed record class MonitorKey
{
    /// <summary>
    /// Device identifier exposed by the operating system.
    /// </summary>
    public string DeviceId { get; init; } = string.Empty;

    /// <summary>
    /// Zero-based index that represents the monitor position.
    /// </summary>
    public int DisplayIndex { get; init; }
}
