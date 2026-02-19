using Microsoft.EntityFrameworkCore;
using Mieruka.Core.Data.Entities;
using Mieruka.Core.Data.Repositories;
using Serilog;

namespace Mieruka.Core.Data.Services;

/// <summary>
/// Serviço CRUD para gerenciamento do inventário de itens.
/// </summary>
public sealed class InventoryService
{
    private static readonly ILogger Logger = Log.ForContext<InventoryService>();

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
        => await _repository.FindAsync(i => i.Category == category, ct);

    public async Task<IReadOnlyList<InventoryItemEntity>> GetByCategoryIdAsync(
        int categoryId, CancellationToken ct = default)
        => await _repository.FindAsync(i => i.CategoryId == categoryId, ct);

    public async Task<IReadOnlyList<InventoryItemEntity>> GetByStatusAsync(
        string status, CancellationToken ct = default)
        => await _repository.FindAsync(i => i.Status == status, ct);

    public async Task<IReadOnlyList<InventoryItemEntity>> SearchAsync(
        string searchTerm, CancellationToken ct = default)
    {
        var pattern = $"%{searchTerm}%";
        return await _context.InventoryItems
            .AsNoTracking()
            .Where(i =>
                EF.Functions.Like(i.Name, pattern) ||
                (i.Description != null && EF.Functions.Like(i.Description, pattern)) ||
                (i.SerialNumber != null && EF.Functions.Like(i.SerialNumber, pattern)) ||
                (i.AssetTag != null && EF.Functions.Like(i.AssetTag, pattern)) ||
                (i.Location != null && EF.Functions.Like(i.Location, pattern)) ||
                (i.AssignedTo != null && EF.Functions.Like(i.AssignedTo, pattern)) ||
                (i.Manufacturer != null && EF.Functions.Like(i.Manufacturer, pattern)))
            .OrderBy(i => i.Name)
            .ToListAsync(ct);
    }

    public async Task<InventoryItemEntity> CreateAsync(InventoryItemEntity item, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ValidateItem(item);

        // Auto-assign CategoryId from Category name if not set.
        if (item.CategoryId is null && !string.IsNullOrWhiteSpace(item.Category))
        {
            var cat = await _context.InventoryCategories
                .FirstOrDefaultAsync(c => c.Name == item.Category, ct)
                .ConfigureAwait(false);
            if (cat is not null)
            {
                item.CategoryId = cat.Id;
            }
        }

        // Auto-assign ItemNumber within category.
        if (item.ItemNumber == 0 && item.CategoryId.HasValue)
        {
            var maxNum = await _context.InventoryItems
                .Where(i => i.CategoryId == item.CategoryId)
                .Select(i => (int?)i.ItemNumber)
                .MaxAsync(ct)
                .ConfigureAwait(false);
            item.ItemNumber = (maxNum ?? 0) + 1;
        }

        item.CreatedAt = DateTime.UtcNow;
        item.UpdatedAt = DateTime.UtcNow;

        var created = await _repository.AddAsync(item, ct);
        Logger.Information("Item de inventário criado: {ItemId} '{ItemName}' na categoria '{Category}'",
            created.Id, created.Name, created.Category);
        return created;
    }

    public async Task UpdateAsync(InventoryItemEntity item, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ValidateItem(item);

        item.UpdatedAt = DateTime.UtcNow;
        await _repository.UpdateAsync(item, ct);
        Logger.Information("Item de inventário atualizado: {ItemId} '{ItemName}'", item.Id, item.Name);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        Logger.Information("Item de inventário excluído: {ItemId}", id);
        await _repository.DeleteAsync(id, ct);
    }

    public Task<int> CountAsync(CancellationToken ct = default)
        => _repository.CountAsync(ct);

    /// <summary>
    /// Retorna itens com garantia que expira nos próximos N dias.
    /// </summary>
    public async Task<IReadOnlyList<InventoryItemEntity>> GetExpiringWarrantyAsync(
        int daysAhead = 30, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(daysAhead);
        return await _context.InventoryItems
            .AsNoTracking()
            .Where(i => i.WarrantyExpiresAt != null && i.WarrantyExpiresAt <= cutoff && i.Status != InventoryItemStatus.Disposed)
            .OrderBy(i => i.WarrantyExpiresAt)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Retorna itens adquiridos em um período.
    /// </summary>
    public async Task<IReadOnlyList<InventoryItemEntity>> GetAcquiredBetweenAsync(
        DateTime from, DateTime to, CancellationToken ct = default)
    {
        return await _context.InventoryItems
            .AsNoTracking()
            .Where(i => i.AcquiredAt >= from && i.AcquiredAt <= to)
            .OrderBy(i => i.AcquiredAt)
            .ToListAsync(ct);
    }

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

    /// <summary>
    /// Retorna o valor total do inventário (soma de UnitCostCents * Quantity).
    /// </summary>
    public async Task<double> GetTotalValueAsync(CancellationToken ct = default)
    {
        var totalCents = await _context.InventoryItems
            .AsNoTracking()
            .Where(i => i.UnitCostCents.HasValue && i.Status != InventoryItemStatus.Disposed)
            .SumAsync(i => (i.UnitCostCents ?? 0) * i.Quantity, ct);
        return totalCents / 100.0;
    }

    /// <summary>
    /// Retorna o resumo de localizações com contagem de itens.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, int>> GetLocationSummaryAsync(CancellationToken ct = default)
        => await _context.InventoryItems
            .AsNoTracking()
            .Where(i => i.Location != null && i.Location != string.Empty)
            .GroupBy(i => i.Location!)
            .ToDictionaryAsync(g => g.Key, g => g.Count(), ct);

    /// <summary>
    /// Verifica se já existe item com o mesmo Nº de Série.
    /// </summary>
    public async Task<bool> SerialNumberExistsAsync(string serialNumber, int? excludeId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(serialNumber)) return false;
        var query = _context.InventoryItems.Where(i => i.SerialNumber == serialNumber);
        if (excludeId.HasValue)
            query = query.Where(i => i.Id != excludeId.Value);
        return await query.AnyAsync(ct);
    }

    /// <summary>
    /// Verifica se já existe item com o mesmo Patrimônio.
    /// </summary>
    public async Task<bool> AssetTagExistsAsync(string assetTag, int? excludeId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(assetTag)) return false;
        var query = _context.InventoryItems.Where(i => i.AssetTag == assetTag);
        if (excludeId.HasValue)
            query = query.Where(i => i.Id != excludeId.Value);
        return await query.AnyAsync(ct);
    }

    private static void ValidateItem(InventoryItemEntity item)
    {
        if (string.IsNullOrWhiteSpace(item.Name))
            throw new ArgumentException("O nome do item é obrigatório.", nameof(item));

        if (item.Quantity < 0)
            throw new ArgumentException("A quantidade não pode ser negativa.", nameof(item));

        if (item.UnitCostCents.HasValue && item.UnitCostCents.Value < 0)
            throw new ArgumentException("O custo unitário não pode ser negativo.", nameof(item));

        if (!string.IsNullOrWhiteSpace(item.Status) &&
            !InventoryItemStatus.All.Contains(item.Status, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Status inválido: '{item.Status}'. Valores válidos: {string.Join(", ", InventoryItemStatus.All)}", nameof(item));
        }
    }
}
