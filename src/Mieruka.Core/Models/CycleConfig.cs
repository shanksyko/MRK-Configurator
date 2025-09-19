using System.Collections.Generic;

namespace Mieruka.Core.Models;

/// <summary>
/// Represents a rotation of items that should be displayed.
/// </summary>
public sealed record class CycleConfig
{
    /// <summary>
    /// Enables or disables the playback cycle.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Default duration for items that omit it, in seconds.
    /// </summary>
    public int DefaultDurationSeconds { get; init; } = 60;

    /// <summary>
    /// Indicates whether the items should be shuffled between iterations.
    /// </summary>
    public bool Shuffle { get; init; }

    /// <summary>
    /// Items that compose the cycle.
    /// </summary>
    public IList<CycleItem> Items { get; init; } = new List<CycleItem>();

    /// <summary>
    /// Keyboard shortcuts that control the playback cycle.
    /// </summary>
    public CycleHotkeyConfig Hotkeys { get; init; } = new();
}
