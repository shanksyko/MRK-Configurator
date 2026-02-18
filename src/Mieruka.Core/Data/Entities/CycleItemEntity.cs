namespace Mieruka.Core.Data.Entities;

/// <summary>
/// Entidade persistida de item de ciclo (equivale a <see cref="Models.CycleItem"/>).
/// </summary>
public sealed class CycleItemEntity
{
    public int Id { get; set; }

    /// <summary>Identificador lógico (CycleItem.Id).</summary>
    public string ExternalId { get; set; } = string.Empty;

    public string TargetId { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public int DurationSeconds { get; set; } = 60;
    public bool Enabled { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
