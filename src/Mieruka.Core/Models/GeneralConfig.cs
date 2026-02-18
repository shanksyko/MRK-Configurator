using System.Collections.Generic;
using System.Text.Json.Serialization;
using Mieruka.Core.Layouts;

namespace Mieruka.Core.Models;

/// <summary>
/// Root configuration for the configurator application.
/// </summary>
public sealed record class GeneralConfig
{
    /// <summary>
    /// Schema version identifier for the configuration.
    /// </summary>
    public string SchemaVersion { get; init; } = ConfigSchemaVersion.Latest;

    /// <summary>
    /// Legacy version identifier preserved for backward compatibility.
    /// </summary>
    [JsonPropertyName("Version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyVersion { get; init; } = null;

    /// <summary>
    /// Describes the monitors available in the environment.
    /// </summary>
    public IReadOnlyList<MonitorInfo> Monitors { get; init; } = [];

    /// <summary>
    /// Applications managed by the configurator.
    /// </summary>
    public IReadOnlyList<AppConfig> Applications { get; init; } = [];

    /// <summary>
    /// Global browser arguments applied to launched sites.
    /// </summary>
    public BrowserArgumentsSettings BrowserArguments { get; init; } = new();

    /// <summary>
    /// Sites managed by the configurator.
    /// </summary>
    public IReadOnlyList<SiteConfig> Sites { get; init; } = [];

    /// <summary>
    /// Zone presets available for layouts.
    /// </summary>
    public IReadOnlyList<ZonePreset> ZonePresets { get; init; } = new List<ZonePreset>(ZonePreset.Defaults);

    /// <summary>
    /// Playback cycle definition.
    /// </summary>
    public CycleConfig Cycle { get; init; } = new();

    /// <summary>
    /// Auto-update settings applied to the configurator.
    /// </summary>
    public UpdateConfig AutoUpdate { get; init; } = new();
}
