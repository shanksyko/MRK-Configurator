namespace Mieruka.Core.Data.Entities;

/// <summary>
/// Registra a movimentação de um item de inventário (transferência, manutenção, baixa, etc.).
/// </summary>
public sealed class InventoryMovementEntity
{
    public int Id { get; set; }

    /// <summary>FK para o item movimentado.</summary>
    public int ItemId { get; set; }

    /// <summary>Tipo da movimentação.</summary>
    public string MovementType { get; set; } = "Transfer";

    /// <summary>Local de origem.</summary>
    public string? FromLocation { get; set; }

    /// <summary>Local de destino.</summary>
    public string? ToLocation { get; set; }

    /// <summary>Responsável anterior.</summary>
    public string? FromAssignee { get; set; }

    /// <summary>Novo responsável.</summary>
    public string? ToAssignee { get; set; }

    /// <summary>Quem realizou a movimentação.</summary>
    public string? PerformedBy { get; set; }

    /// <summary>Observações da movimentação.</summary>
    public string? Notes { get; set; }

    public DateTime MovedAt { get; set; } = DateTime.UtcNow;
}
