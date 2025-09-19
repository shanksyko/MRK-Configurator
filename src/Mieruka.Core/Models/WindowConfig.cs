namespace Mieruka.Core.Models;

/// <summary>
/// Defines how a window should be positioned.
/// </summary>
public sealed record class WindowConfig
{
    /// <summary>
    /// Optional logical name for the window.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Target monitor that should display the window.
    /// </summary>
    public MonitorKey Monitor { get; init; } = new();

    /// <summary>
    /// Optional X coordinate relative to the monitor.
    /// </summary>
    public int? X { get; init; }

    /// <summary>
    /// Optional Y coordinate relative to the monitor.
    /// </summary>
    public int? Y { get; init; }

    /// <summary>
    /// Optional width for the window.
    /// </summary>
    public int? Width { get; init; }

    /// <summary>
    /// Optional height for the window.
    /// </summary>
    public int? Height { get; init; }

    /// <summary>
    /// Indicates whether the window should run in fullscreen mode.
    /// </summary>
    public bool FullScreen { get; init; }

    /// <summary>
    /// Indicates whether the window should stay on top of other windows.
    /// </summary>
    public bool AlwaysOnTop { get; init; }
}
