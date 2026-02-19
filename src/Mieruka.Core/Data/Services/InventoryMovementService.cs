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

public sealed class InventoryMovementService
{
    private static readonly ILogger Logger = Log.ForContext<InventoryMovementService>();
    private readonly MierukaDbContext _context;

    public InventoryMovementService(MierukaDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<InventoryMovementEntity> RecordMovementAsync(
        int itemId,
        string movementType,
        string? fromLocation,
        string? toLocation,
        string? fromAssignee,
        string? toAssignee,
        string? performedBy,
        string? notes = null,
        CancellationToken ct = default)
    {
        var movement = new InventoryMovementEntity
        {
            ItemId = itemId,
            MovementType = movementType,
            FromLocation = fromLocation,
            ToLocation = toLocation,
            FromAssignee = fromAssignee,
            ToAssignee = toAssignee,
            PerformedBy = performedBy,
            Notes = notes,
            MovedAt = DateTime.UtcNow,
        };

        _context.InventoryMovements.Add(movement);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
        Logger.Information("Movimentação registrada: Item {ItemId} tipo '{MovementType}' de '{From}' para '{To}' por '{PerformedBy}'",
            movement.ItemId, movement.MovementType,
            movement.FromLocation ?? movement.FromAssignee,
            movement.ToLocation ?? movement.ToAssignee,
            movement.PerformedBy);
        return movement;
    }

    public async Task<IReadOnlyList<InventoryMovementEntity>> GetMovementHistoryAsync(
        int itemId,
        CancellationToken ct = default)
    {
        return await _context.InventoryMovements
            .AsNoTracking()
            .Where(m => m.ItemId == itemId)
            .OrderByDescending(m => m.MovedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<InventoryMovementEntity>> GetRecentMovementsAsync(
        int limit = 50,
        CancellationToken ct = default)
    {
        return await _context.InventoryMovements
            .AsNoTracking()
            .OrderByDescending(m => m.MovedAt)
            .Take(limit)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }
}
