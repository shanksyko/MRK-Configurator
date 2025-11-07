using System.Text.Json.Serialization;

namespace Mieruka.Core.Config;

/// <summary>
/// Defines application-wide configuration settings for the configurator.
/// </summary>
public sealed record class AppConfig
{
    /// <summary>
    /// Gets preview-related configuration options.
    /// </summary>
    public PreviewConfig Preview { get; init; } = new();
}

/// <summary>
/// Describes how monitor previews should be rendered.
/// </summary>
public sealed record class PreviewConfig
{
    /// <summary>
    /// Determines the preferred hardware acceleration mode for previews.
    /// </summary>
    public HardwareAccelerationMode HardwareAccelerationMode { get; init; } = HardwareAccelerationMode.Auto;

    /// <summary>
    /// Indicates whether the last preview run required forcing the GDI path.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LastRunForcedGdi { get; init; }
}

/// <summary>
/// Enumerates the hardware acceleration modes available for monitor previews.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HardwareAccelerationMode
{
    /// <summary>
    /// Uses GPU capture when available and falls back to GDI when required.
    /// </summary>
    Auto,

    /// <summary>
    /// Always prefer GPU capture, even after previous fallbacks.
    /// </summary>
    PreferGpu,

    /// <summary>
    /// Always use the GDI fallback mode.
    /// </summary>
    PreferGdi,
}
