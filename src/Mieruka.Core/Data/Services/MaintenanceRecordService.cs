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

public static class MaintenanceStatus
{
    public const string Scheduled = "Scheduled";
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
}

public static class InventoryItemStatus
{
    public const string Active = "Active";
    public const string Inactive = "Inactive";
    public const string UnderMaintenance = "UnderMaintenance";
    public const string Disposed = "Disposed";
    public const string InStock = "InStock";

    public static readonly string[] All = [Active, Inactive, UnderMaintenance, Disposed, InStock];
}

public sealed class MaintenanceRecordService
{
    private static readonly ILogger Logger = Log.ForContext<MaintenanceRecordService>();
    private readonly MierukaDbContext _context;

    public MaintenanceRecordService(MierukaDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IReadOnlyList<MaintenanceRecordEntity>> GetByItemAsync(
        int itemId,
        CancellationToken ct = default)
    {
        return await _context.MaintenanceRecords
            .AsNoTracking()
            .Where(m => m.ItemId == itemId)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MaintenanceRecordEntity>> GetScheduledAsync(
        CancellationToken ct = default)
    {
        return await _context.MaintenanceRecords
            .AsNoTracking()
            .Where(m => m.Status == MaintenanceStatus.Scheduled || m.Status == MaintenanceStatus.InProgress)
            .OrderBy(m => m.ScheduledAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MaintenanceRecordEntity>> GetOverdueAsync(
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _context.MaintenanceRecords
            .AsNoTracking()
            .Where(m => m.ScheduledAt < now && m.Status != MaintenanceStatus.Completed && m.Status != MaintenanceStatus.Cancelled)
            .OrderBy(m => m.ScheduledAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<MaintenanceRecordEntity> CreateAsync(
        MaintenanceRecordEntity record,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        record.CreatedAt = DateTime.UtcNow;

        _context.MaintenanceRecords.Add(record);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
        Logger.Information("Manutenção criada: {RecordId} Item {ItemId} tipo '{MaintenanceType}' status '{Status}'",
            record.Id, record.ItemId, record.MaintenanceType, record.Status);
        return record;
    }

    public async Task UpdateAsync(
        MaintenanceRecordEntity record,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        _context.MaintenanceRecords.Update(record);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
        Logger.Information("Manutenção atualizada: {RecordId} Item {ItemId} status '{Status}'",
            record.Id, record.ItemId, record.Status);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await _context.MaintenanceRecords.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
        if (entity is not null)
        {
            _context.MaintenanceRecords.Remove(entity);
            await _context.SaveChangesAsync(ct).ConfigureAwait(false);
            Logger.Information("Manutenção excluída: {RecordId} Item {ItemId}", entity.Id, entity.ItemId);
        }
    }

    public async Task<double> GetTotalCostByItemAsync(int itemId, CancellationToken ct = default)
    {
        var totalCents = await _context.MaintenanceRecords
            .AsNoTracking()
            .Where(m => m.ItemId == itemId && m.CostCents.HasValue)
            .SumAsync(m => m.CostCents ?? 0, ct)
            .ConfigureAwait(false);

        return totalCents / 100.0;
    }
}
