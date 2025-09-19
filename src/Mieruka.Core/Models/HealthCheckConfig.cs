using System;

namespace Mieruka.Core.Models;

/// <summary>
/// Defines health check rules applied to entries supervised by the watchdog service.
/// </summary>
public sealed record class HealthCheckConfig
{
    /// <summary>
    /// Gets the type of health check that should be executed.
    /// </summary>
    public HealthCheckKind Type { get; init; } = HealthCheckKind.None;

    /// <summary>
    /// Optional target URL that should be queried during health checks.
    /// When not provided, the watchdog falls back to the primary URL associated with the entry.
    /// </summary>
    public string? Url { get; init; }

    /// <summary>
    /// Optional DOM selector or snippet that must be present when using <see cref="HealthCheckKind.Dom"/>.
    /// </summary>
    public string? DomSelector { get; init; }

    /// <summary>
    /// Optional textual snippet expected to be contained in the HTML payload when using <see cref="HealthCheckKind.Dom"/>.
    /// </summary>
    public string? ContainsText { get; init; }

    /// <summary>
    /// Interval in seconds between consecutive health checks.
    /// </summary>
    public int IntervalSeconds { get; init; } = 60;

    /// <summary>
    /// Timeout in seconds applied to HTTP operations executed by the health check.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 10;
}
