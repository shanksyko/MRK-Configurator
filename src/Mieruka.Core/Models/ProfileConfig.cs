using System.Collections.Generic;

namespace Mieruka.Core.Models;

/// <summary>
/// Represents a launch profile composed of native applications and window layouts.
/// </summary>
public sealed record class ProfileConfig
{
    /// <summary>
    /// Unique identifier of the profile.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Human friendly name associated with the profile.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Schema version associated with the profile payload.
    /// </summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>
    /// Applications that should be launched when the profile is executed.
    /// </summary>
    public IReadOnlyList<AppConfig> Applications { get; init; } = [];

    /// <summary>
    /// Standalone window placements that should be enforced after launching the applications.
    /// </summary>
    public IReadOnlyList<WindowConfig> Windows { get; init; } = [];

    /// <summary>
    /// Optional identifier of the monitor used when an entry does not specify a target monitor.
    /// </summary>
    public string? DefaultMonitorId { get; init; }
}
