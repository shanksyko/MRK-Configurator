using System;
using System.Threading.Tasks;

namespace Mieruka.Core.Security;

/// <summary>
/// Ensures browser and driver versions remain compatible.
/// </summary>
public sealed class DriverVersionGuard
{
    private readonly VersionTolerance _tolerance;

    /// <summary>
    /// Initializes a new instance of the <see cref="DriverVersionGuard"/> class.
    /// </summary>
    public DriverVersionGuard(VersionTolerance? tolerance = null)
    {
        _tolerance = tolerance ?? new VersionTolerance(0, 1);
    }

    /// <summary>
    /// Validates that the supplied versions are compatible.
    /// </summary>
    public void EnsureCompatible(Version browserVersion, Version driverVersion)
    {
        ArgumentNullException.ThrowIfNull(browserVersion);
        ArgumentNullException.ThrowIfNull(driverVersion);

        if (Math.Abs(browserVersion.Major - driverVersion.Major) > _tolerance.Major)
        {
            throw new DriverVersionMismatchException(browserVersion, driverVersion, "Major versions differ.");
        }

        if (Math.Abs(browserVersion.Minor - driverVersion.Minor) > _tolerance.Minor)
        {
            throw new DriverVersionMismatchException(browserVersion, driverVersion, "Minor versions differ beyond tolerance.");
        }
    }

    /// <summary>
    /// Attempts to update the driver when a mismatch occurs.
    /// </summary>
    public async Task<bool> TryAutoUpdateAsync(Func<Task> updater)
    {
        ArgumentNullException.ThrowIfNull(updater);
        try
        {
            await updater().ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Represents tolerance for version differences.
/// </summary>
public sealed record class VersionTolerance(int Major, int Minor);

/// <summary>
/// Exception thrown when driver versions are not compatible.
/// </summary>
public sealed class DriverVersionMismatchException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DriverVersionMismatchException"/> class.
    /// </summary>
    public DriverVersionMismatchException(Version browser, Version driver, string reason)
        : base($"Driver version mismatch: browser={browser} driver={driver}. {reason}")
    {
    }
}
