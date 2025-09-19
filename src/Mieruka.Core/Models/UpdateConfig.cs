using System.Text.Json.Serialization;

namespace Mieruka.Core.Models;

/// <summary>
/// Describes configuration parameters for the application auto-update mechanism.
/// </summary>
public sealed record class UpdateConfig
{
    private const int DefaultIntervalMinutes = 60;

    /// <summary>
    /// Gets a value indicating whether the auto-update mechanism is enabled.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Gets the endpoint that exposes the update manifest in JSON format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ManifestUrl { get; init; }

    /// <summary>
    /// Gets the interval, in minutes, between update checks.
    /// </summary>
    public int CheckIntervalMinutes { get; init; } = DefaultIntervalMinutes;
}
