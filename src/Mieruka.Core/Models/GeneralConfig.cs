using System.Collections.Generic;

namespace Mieruka.Core.Models;

/// <summary>
/// Root configuration for the configurator application.
/// </summary>
public sealed record class GeneralConfig
{
    /// <summary>
    /// Optional version identifier for the configuration.
    /// </summary>
    public string Version { get; init; } = "1.0";

    /// <summary>
    /// Describes the monitors available in the environment.
    /// </summary>
    public IList<MonitorInfo> Monitors { get; init; } = new List<MonitorInfo>();

    /// <summary>
    /// Applications managed by the configurator.
    /// </summary>
    public IList<AppConfig> Applications { get; init; } = new List<AppConfig>();

    /// <summary>
    /// Sites managed by the configurator.
    /// </summary>
    public IList<SiteConfig> Sites { get; init; } = new List<SiteConfig>();

    /// <summary>
    /// Playback cycle definition.
    /// </summary>
    public CycleConfig Cycle { get; init; } = new();
}
