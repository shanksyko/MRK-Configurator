namespace Mieruka.Core.Data.Entities;

/// <summary>
/// Armazena configurações gerais como pares chave-valor com JSON.
/// Cobre BrowserArguments, CycleConfig (hotkeys, duração), UpdateConfig, etc.
/// </summary>
public sealed class AppSettingEntity
{
    public int Id { get; set; }

    /// <summary>Chave única do setting (ex: "CycleConfig", "UpdateConfig", "BrowserArguments").</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Valor serializado em JSON.</summary>
    public string ValueJson { get; set; } = "{}";

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
