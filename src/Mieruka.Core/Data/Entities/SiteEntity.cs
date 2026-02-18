namespace Mieruka.Core.Data.Entities;

/// <summary>
/// Entidade persistida de site/dashboard (equivale a <see cref="Models.SiteConfig"/>).
/// </summary>
public sealed class SiteEntity
{
    public int Id { get; set; }

    /// <summary>Identificador lógico (mapeado de SiteConfig.Id).</summary>
    public string ExternalId { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;
    public string Browser { get; set; } = "Chrome";
    public string? UserDataDirectory { get; set; }
    public string? ProfileDirectory { get; set; }
    public bool AppMode { get; set; }
    public bool KioskMode { get; set; }
    public bool ReloadOnActivate { get; set; }
    public int? ReloadIntervalSeconds { get; set; }
    public string TargetMonitorStableId { get; set; } = string.Empty;
    public string? TargetZonePresetId { get; set; }

    // ── Campos JSON para objetos aninhados ──
    public string BrowserArgumentsJson { get; set; } = "[]";
    public string HeadersJson { get; set; } = "{}";
    public string AllowedTabHostsJson { get; set; } = "[]";
    public string WatchdogJson { get; set; } = "{}";
    public string WindowJson { get; set; } = "{}";
    public string? LoginJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
