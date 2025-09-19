namespace Mieruka.Core.Models;

/// <summary>
/// Well-known schema version identifiers for <see cref="GeneralConfig"/>.
/// </summary>
public static class ConfigSchemaVersion
{
    /// <summary>
    /// Initial configuration schema version.
    /// </summary>
    public const string V1 = "1.0";

    /// <summary>
    /// Current configuration schema version.
    /// </summary>
    public const string V2 = "2.0";

    /// <summary>
    /// Gets the latest schema version supported by the application.
    /// </summary>
    public const string Latest = V2;
}
