namespace Mieruka.Core.Data.Entities;

/// <summary>
/// Entidade persistida de perfil de lançamento (equivale a <see cref="Models.ProfileConfig"/>).
/// As listas de aplicações e janelas são armazenadas como JSON.
/// </summary>
public sealed class ProfileEntity
{
    public int Id { get; set; }

    /// <summary>Identificador lógico (ProfileConfig.Id).</summary>
    public string ExternalId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public int SchemaVersion { get; set; } = 1;
    public string? DefaultMonitorId { get; set; }

    // ── Listas complexas armazenadas como JSON ──
    public string ApplicationsJson { get; set; } = "[]";
    public string WindowsJson { get; set; } = "[]";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
