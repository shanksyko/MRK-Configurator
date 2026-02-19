#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Mieruka.Core.Data.Entities;
using Serilog;

namespace Mieruka.Core.Data.Services;

public sealed class InventoryCategoryService
{
    private static readonly ILogger Logger = Log.ForContext<InventoryCategoryService>();
    private readonly MierukaDbContext _context;

    public InventoryCategoryService(MierukaDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IReadOnlyList<InventoryCategoryEntity>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.InventoryCategories
            .AsNoTracking()
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<InventoryCategoryEntity?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _context.InventoryCategories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<InventoryCategoryEntity> CreateAsync(InventoryCategoryEntity category, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(category);

        var maxOrder = await _context.InventoryCategories
            .Select(c => (int?)c.DisplayOrder)
            .MaxAsync(ct)
            .ConfigureAwait(false);

        category.DisplayOrder = (maxOrder ?? 0) + 1;
        category.CreatedAt = DateTime.UtcNow;
        category.UpdatedAt = DateTime.UtcNow;

        _context.InventoryCategories.Add(category);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
        Logger.Information("Categoria de inventário criada: {CategoryId} '{CategoryName}'", category.Id, category.Name);
        return category;
    }

    public async Task UpdateAsync(InventoryCategoryEntity category, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(category);
        category.UpdatedAt = DateTime.UtcNow;
        _context.InventoryCategories.Update(category);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
        Logger.Information("Categoria de inventário atualizada: {CategoryId} '{CategoryName}'", category.Id, category.Name);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await _context.InventoryCategories.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
        if (entity is not null)
        {
            _context.InventoryCategories.Remove(entity);
            await _context.SaveChangesAsync(ct).ConfigureAwait(false);
            Logger.Information("Categoria de inventário excluída: {CategoryId} '{CategoryName}'", entity.Id, entity.Name);
        }
    }

    public async Task MoveUpAsync(int id, CancellationToken ct = default)
    {
        var all = await _context.InventoryCategories
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var index = all.FindIndex(c => c.Id == id);
        if (index <= 0)
        {
            return;
        }

        var current = all[index];
        var previous = all[index - 1];

        (current.DisplayOrder, previous.DisplayOrder) = (previous.DisplayOrder, current.DisplayOrder);
        current.UpdatedAt = DateTime.UtcNow;
        previous.UpdatedAt = DateTime.UtcNow;

        _context.InventoryCategories.Update(current);
        _context.InventoryCategories.Update(previous);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task MoveDownAsync(int id, CancellationToken ct = default)
    {
        var all = await _context.InventoryCategories
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var index = all.FindIndex(c => c.Id == id);
        if (index < 0 || index >= all.Count - 1)
        {
            return;
        }

        var current = all[index];
        var next = all[index + 1];

        (current.DisplayOrder, next.DisplayOrder) = (next.DisplayOrder, current.DisplayOrder);
        current.UpdatedAt = DateTime.UtcNow;
        next.UpdatedAt = DateTime.UtcNow;

        _context.InventoryCategories.Update(current);
        _context.InventoryCategories.Update(next);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
