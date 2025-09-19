namespace Mieruka.Core.Models;

/// <summary>
/// Defines configurable keyboard shortcuts used to control cycle playback.
/// </summary>
public sealed record class CycleHotkeyConfig
{
    /// <summary>
    /// Key combination that toggles the playback state.
    /// </summary>
    public string? PlayPause { get; init; } = "Ctrl+Alt+P";

    /// <summary>
    /// Key combination that advances to the next item in the cycle.
    /// </summary>
    public string? Next { get; init; } = "Ctrl+Alt+Right";

    /// <summary>
    /// Key combination that returns to the previous item in the cycle.
    /// </summary>
    public string? Previous { get; init; } = "Ctrl+Alt+Left";
}
