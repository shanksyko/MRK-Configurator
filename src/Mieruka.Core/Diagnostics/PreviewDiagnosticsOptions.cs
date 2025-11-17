using System.Text.Json.Serialization;

namespace Mieruka.Core.Diagnostics;

/// <summary>
/// Provides diagnostic-related options for monitor preview operations.
/// </summary>
public sealed record class PreviewDiagnosticsOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether extreme logging should be enabled.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ExtremeLoggingEnabled { get; init; }

    /// <summary>
    /// Normalizes the current options ensuring all values are present.
    /// </summary>
    /// <returns>A sanitized <see cref="PreviewDiagnosticsOptions"/> instance.</returns>
    public PreviewDiagnosticsOptions Normalize()
        => this;
}
