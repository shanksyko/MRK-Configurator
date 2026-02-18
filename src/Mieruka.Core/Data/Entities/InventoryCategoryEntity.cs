namespace Mieruka.Core.Data.Entities;

/// <summary>
/// Categoria definida pelo usuário para agrupar itens de inventário.
/// Cada categoria gera uma aba dinâmica na UI (ex: "Monitores", "PCs", "Switches").
/// </summary>
public sealed class InventoryCategoryEntity
{
    public int Id { get; set; }

    /// <summary>Nome visível da categoria (ex: "Monitores", "PCs", "Cabos").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Ícone opcional (nome do ícone ou emoji).</summary>
    public string? Icon { get; set; }

    /// <summary>Descrição livre da categoria.</summary>
    public string? Description { get; set; }

    /// <summary>Ordem de exibição nas abas.</summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Campos personalizados definidos pelo usuário para esta categoria, em JSON.
    /// Ex: [{"name":"Resolução","type":"text"},{"name":"Polegadas","type":"number"}]
    /// </summary>
    public string? CustomFieldsJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
