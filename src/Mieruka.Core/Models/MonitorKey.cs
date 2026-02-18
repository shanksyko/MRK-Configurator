namespace Mieruka.Core.Models;

/// <summary>
/// Identifies a monitor in the system.
/// </summary>
public readonly record struct MonitorKey()
{
    /// <summary>
    /// Device identifier exposed by the operating system.
    /// </summary>
    public string DeviceId { get; init; } = string.Empty;

    /// <summary>
    /// Zero-based index that represents the monitor position.
    /// </summary>
    public int DisplayIndex { get; init; }

    /// <summary>
    /// High-order bits of the adapter <c>LUID</c> that owns the monitor target.
    /// </summary>
    public int AdapterLuidHigh { get; init; }

    /// <summary>
    /// Low-order bits of the adapter <c>LUID</c> that owns the monitor target.
    /// </summary>
    public int AdapterLuidLow { get; init; }

    /// <summary>
    /// Identifier of the monitor target within the adapter.
    /// </summary>
    public int TargetId { get; init; }
}
