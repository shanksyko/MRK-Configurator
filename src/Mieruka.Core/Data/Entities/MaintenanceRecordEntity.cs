namespace Mieruka.Core.Data.Entities;

/// <summary>
/// Registro de manutenção preventiva ou corretiva de um item de inventário.
/// </summary>
public sealed class MaintenanceRecordEntity
{
    public int Id { get; set; }

    /// <summary>FK para o item.</summary>
    public int ItemId { get; set; }

    /// <summary>Navigation property para o item.</summary>
    public InventoryItemEntity? Item { get; set; }

    /// <summary>Tipo: Preventive, Corrective, Inspection.</summary>
    public string MaintenanceType { get; set; } = "Corrective";

    /// <summary>Descrição do serviço realizado.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Quem realizou a manutenção.</summary>
    public string? PerformedBy { get; set; }

    /// <summary>Custo da manutenção (em centavos para evitar floating-point).</summary>
    public long? CostCents { get; set; }

    /// <summary>Status: Scheduled, InProgress, Completed, Cancelled.</summary>
    public string Status { get; set; } = "Completed";

    /// <summary>Data agendada (quando preventiva).</summary>
    public DateTime? ScheduledAt { get; set; }

    /// <summary>Data de conclusão.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Observações adicionais.</summary>
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
