using System.Net.NetworkInformation;

namespace Mieruka.Core.Services;

/// <summary>
/// Default implementation of <see cref="INetworkAvailabilityService"/> based on the local network stack.
/// </summary>
public sealed class NetworkAvailabilityService : INetworkAvailabilityService
{
    /// <inheritdoc />
    public bool IsNetworkAvailable()
    {
        try
        {
            return NetworkInterface.GetIsNetworkAvailable();
        }
        catch
        {
            return true;
        }
    }
}
