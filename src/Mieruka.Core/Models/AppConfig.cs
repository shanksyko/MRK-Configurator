using System.Collections.Generic;

namespace Mieruka.Core.Models;

/// <summary>
/// Configuration settings for a native application.
/// </summary>
public sealed record class AppConfig
{
    /// <summary>
    /// Unique identifier for the application entry.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Path to the executable.
    /// </summary>
    public string ExecutablePath { get; init; } = string.Empty;

    /// <summary>
    /// Optional command line arguments.
    /// </summary>
    public string? Arguments { get; init; }

    /// <summary>
    /// Indicates whether the application should start automatically.
    /// </summary>
    public bool AutoStart { get; init; } = true;

    /// <summary>
    /// Optional environment variables applied when launching the application.
    /// </summary>
    public IDictionary<string, string> EnvironmentVariables { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Watchdog related settings that describe how the application should be supervised.
    /// </summary>
    public WatchdogSettings Watchdog { get; init; } = new();

    /// <summary>
    /// Desired window placement.
    /// </summary>
    public WindowConfig Window { get; init; } = new();

    /// <summary>
    /// Stable identifier of the monitor that should host the application.
    /// </summary>
    public string TargetMonitorStableId { get; init; } = string.Empty;

    /// <summary>
    /// Identifier of the zone preset applied when positioning the application window.
    /// </summary>
    public string? TargetZonePresetId { get; init; }
}
