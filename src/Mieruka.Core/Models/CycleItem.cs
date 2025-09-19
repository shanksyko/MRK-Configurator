namespace Mieruka.Core.Models;

/// <summary>
/// Defines an item that can be displayed during a playback cycle.
/// </summary>
public sealed record class CycleItem
{
    /// <summary>
    /// Unique identifier for the cycle item.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Target identifier, such as an application or site id.
    /// </summary>
    public string TargetId { get; init; } = string.Empty;

    /// <summary>
    /// Logical classification of the target.
    /// </summary>
    public string TargetType { get; init; } = string.Empty;

    /// <summary>
    /// Time the item should stay active, in seconds.
    /// </summary>
    public int DurationSeconds { get; init; } = 60;

    /// <summary>
    /// Indicates whether the item is active in the rotation.
    /// </summary>
    public bool Enabled { get; init; } = true;
}
