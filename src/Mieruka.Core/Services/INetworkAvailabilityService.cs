namespace Mieruka.Core.Services;

/// <summary>
/// Provides information about the current network state.
/// </summary>
public interface INetworkAvailabilityService
{
    /// <summary>
    /// Determines whether the network is currently available.
    /// </summary>
    /// <returns><c>true</c> when the network is available; otherwise <c>false</c>.</returns>
    bool IsNetworkAvailable();
}
