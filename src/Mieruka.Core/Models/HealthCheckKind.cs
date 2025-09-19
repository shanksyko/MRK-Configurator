namespace Mieruka.Core.Models;

/// <summary>
/// Represents the strategy used to verify the health of an entry supervised by the watchdog.
/// </summary>
public enum HealthCheckKind
{
    /// <summary>
    /// Health checks are disabled for the entry.
    /// </summary>
    None,

    /// <summary>
    /// Performs an HTTP ping against a target endpoint, considering the entry healthy when the response succeeds.
    /// </summary>
    Ping,

    /// <summary>
    /// Performs an HTTP request and validates the returned markup against an expected DOM selector or snippet.
    /// </summary>
    Dom,
}
