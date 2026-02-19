namespace Mieruka.Core.Data.Entities;

/// <summary>
/// Entidade de inventário — permite rastrear equipamentos, licenças,
/// monitores físicos, PCs e qualquer item associado ao ambiente.
/// </summary>
public sealed class InventoryItemEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>FK para a categoria definida pelo usuário.</summary>
    public int? CategoryId { get; set; }

    /// <summary>Navigation property para a categoria.</summary>
    public InventoryCategoryEntity? CategoryNavigation { get; set; }

    /// <summary>Categoria livre (ex: "Monitor", "PC", "Licença", "Cabo", "Periférico").</summary>
    public string Category { get; set; } = string.Empty;

    public string? Description { get; set; }
    public string? SerialNumber { get; set; }
    public string? AssetTag { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? Location { get; set; }

    /// <summary>Status do item: Active, Inactive, Maintenance, Retired, InStock.</summary>
    public string Status { get; set; } = "Active";

    public int Quantity { get; set; } = 1;
    public string? AssignedTo { get; set; }
    public string? Notes { get; set; }

    /// <summary>StableId do monitor associado (quando o item é vinculado a uma estação).</summary>
    public string? LinkedMonitorStableId { get; set; }

    /// <summary>Número/índice dentro da categoria (ex: Monitor 1, Monitor 2).</summary>
    public int ItemNumber { get; set; }

    /// <summary>Metadados adicionais flexíveis em JSON.</summary>
    public string? MetadataJson { get; set; }

    /// <summary>Valores de campos customizados da categoria, em JSON.</summary>
    public string? CustomFieldValuesJson { get; set; }

    /// <summary>Valor unitário do item (em centavos para evitar floating-point).</summary>
    public long? UnitCostCents { get; set; }

    public DateTime? AcquiredAt { get; set; }
    public DateTime? WarrantyExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Navigation: movimentações deste item.</summary>
    public ICollection<InventoryMovementEntity> Movements { get; set; } = new List<InventoryMovementEntity>();

    /// <summary>Navigation: registros de manutenção deste item.</summary>
    public ICollection<MaintenanceRecordEntity> MaintenanceRecords { get; set; } = new List<MaintenanceRecordEntity>();
}
