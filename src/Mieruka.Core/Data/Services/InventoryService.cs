using Microsoft.EntityFrameworkCore;
using Mieruka.Core.Data.Entities;
using Mieruka.Core.Data.Repositories;

namespace Mieruka.Core.Data.Services;

/// <summary>
/// Serviço CRUD para gerenciamento do inventário de itens.
/// </summary>
public sealed class InventoryService
{
    private readonly IRepository<InventoryItemEntity> _repository;
    private readonly MierukaDbContext _context;

    public InventoryService(MierukaDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _repository = new Repository<InventoryItemEntity>(context);
    }

    public Task<InventoryItemEntity?> GetByIdAsync(int id, CancellationToken ct = default)
        => _repository.GetByIdAsync(id, ct);

    public Task<IReadOnlyList<InventoryItemEntity>> GetAllAsync(CancellationToken ct = default)
        => _repository.GetAllAsync(ct);

    public async Task<IReadOnlyList<InventoryItemEntity>> GetByCategoryAsync(
        string category, CancellationToken ct = default)
        => await _repository.FindAsync(
            i => i.Category == category, ct);

    public async Task<IReadOnlyList<InventoryItemEntity>> GetByStatusAsync(
        string status, CancellationToken ct = default)
        => await _repository.FindAsync(
            i => i.Status == status, ct);

    public async Task<IReadOnlyList<InventoryItemEntity>> SearchAsync(
        string searchTerm, CancellationToken ct = default)
    {
        // EF.Functions.Like é traduzido para LIKE no SQLite — case-insensitive por padrão.
        var pattern = $"%{searchTerm}%";
        return await _context.InventoryItems
            .AsNoTracking()
            .Where(i =>
                EF.Functions.Like(i.Name, pattern) ||
                (i.Description != null && EF.Functions.Like(i.Description, pattern)) ||
                (i.SerialNumber != null && EF.Functions.Like(i.SerialNumber, pattern)) ||
                (i.AssetTag != null && EF.Functions.Like(i.AssetTag, pattern)) ||
                (i.Location != null && EF.Functions.Like(i.Location, pattern)))
            .OrderBy(i => i.Name)
            .ToListAsync(ct);
    }

    public async Task<InventoryItemEntity> CreateAsync(InventoryItemEntity item, CancellationToken ct = default)
    {
        item.CreatedAt = DateTime.UtcNow;
        item.UpdatedAt = DateTime.UtcNow;
        return await _repository.AddAsync(item, ct);
    }

    public async Task UpdateAsync(InventoryItemEntity item, CancellationToken ct = default)
    {
        item.UpdatedAt = DateTime.UtcNow;
        await _repository.UpdateAsync(item, ct);
    }

    public Task DeleteAsync(int id, CancellationToken ct = default)
        => _repository.DeleteAsync(id, ct);

    public Task<int> CountAsync(CancellationToken ct = default)
        => _repository.CountAsync(ct);

    /// <summary>
    /// Retorna um resumo de contagem por categoria.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, int>> GetCategorySummaryAsync(CancellationToken ct = default)
        => await _context.InventoryItems
            .AsNoTracking()
            .GroupBy(i => i.Category)
            .ToDictionaryAsync(g => g.Key, g => g.Count(), ct);

    /// <summary>
    /// Retorna um resumo de contagem por status.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, int>> GetStatusSummaryAsync(CancellationToken ct = default)
        => await _context.InventoryItems
            .AsNoTracking()
            .GroupBy(i => i.Status)
            .ToDictionaryAsync(g => g.Key, g => g.Count(), ct);
}
