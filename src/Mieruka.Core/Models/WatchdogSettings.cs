namespace Mieruka.Core.Models;

/// <summary>
/// Represents watchdog specific settings for a configuration entry.
/// </summary>
public sealed record class WatchdogSettings
{
    /// <summary>
    /// Indicates whether the watchdog supervision is enabled for the entry.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Optional grace period, in seconds, applied after a successful restart before health checks resume.
    /// </summary>
    public int RestartGracePeriodSeconds { get; init; } = 15;

    /// <summary>
    /// Maximum number of consecutive restart attempts before giving up (0 = unlimited).
    /// </summary>
    public int MaxRestartAttempts { get; init; }

    /// <summary>
    /// Interval in seconds to periodically re-check and reposition the window (0 = disabled).
    /// </summary>
    public int WindowRecheckIntervalSeconds { get; init; }

    /// <summary>
    /// Defines the health check behaviour executed while the entry is running.
    /// </summary>
    public HealthCheckConfig? HealthCheck { get; init; }
}
