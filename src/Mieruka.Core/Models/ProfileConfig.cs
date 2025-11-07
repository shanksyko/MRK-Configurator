using System.Collections.Generic;
using ConfigAppConfig = Mieruka.Core.Config.AppConfig;

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
    public IList<AppConfig> Applications { get; init; } = new List<AppConfig>();

    /// <summary>
    /// Standalone window placements that should be enforced after launching the applications.
    /// </summary>
    public IList<WindowConfig> Windows { get; init; } = new List<WindowConfig>();

    /// <summary>
    /// Optional identifier of the monitor used when an entry does not specify a target monitor.
    /// </summary>
    public string? DefaultMonitorId { get; init; }

    /// <summary>
    /// Application-level configuration applied while editing and running profiles.
    /// </summary>
    public ConfigAppConfig App { get; init; } = new();
}
