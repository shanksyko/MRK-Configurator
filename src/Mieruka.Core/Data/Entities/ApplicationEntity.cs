namespace Mieruka.Core.Data.Entities;

/// <summary>
/// Entidade persistida de aplicação (equivale a <see cref="Models.AppConfig"/>).
/// Campos complexos (Watchdog, Window, EnvironmentVariables) são armazenados como JSON.
/// </summary>
public sealed class ApplicationEntity
{
    public int Id { get; set; }

    /// <summary>Identificador lógico (mapeado de AppConfig.Id).</summary>
    public string ExternalId { get; set; } = string.Empty;

    public string? Name { get; set; }
    public int Order { get; set; }
    public string ExecutablePath { get; set; } = string.Empty;
    public string? Arguments { get; set; }
    public bool AutoStart { get; set; } = true;
    public bool AskBeforeLaunch { get; set; }
    public bool RequiresNetwork { get; set; }
    public int DelayMs { get; set; }
    public string TargetMonitorStableId { get; set; } = string.Empty;
    public string? TargetZonePresetId { get; set; }

    // ── Campos JSON para objetos aninhados ──
    public string EnvironmentVariablesJson { get; set; } = "{}";
    public string WatchdogJson { get; set; } = "{}";
    public string WindowJson { get; set; } = "{}";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
