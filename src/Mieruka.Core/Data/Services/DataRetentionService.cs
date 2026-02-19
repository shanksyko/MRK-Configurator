using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Mieruka.Core.Data.Services;

/// <summary>
/// Purges old data from the database according to configurable retention policies.
/// </summary>
public sealed class DataRetentionService
{
    private static readonly ILogger Logger = Log.ForContext<DataRetentionService>();
    private const string SettingsKey = "DataRetention";

    private readonly MierukaDbContext _db;

    public DataRetentionService(MierukaDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <summary>
    /// Purges audit log entries older than the specified number of days.
    /// </summary>
    public async Task<int> PurgeAuditLogsAsync(int retentionDays, CancellationToken cancellationToken = default)
    {
        if (retentionDays <= 0) return 0;

        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        var records = await _db.AuditLog
            .Where(a => a.Timestamp < cutoff)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (records.Count == 0) return 0;

        _db.AuditLog.RemoveRange(records);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Logger.Information("Purged {Count} audit log entries older than {Days} days.", records.Count, retentionDays);
        return records.Count;
    }

    /// <summary>
    /// Purges inventory movements older than the specified number of days.
    /// </summary>
    public async Task<int> PurgeMovementsAsync(int retentionDays, CancellationToken cancellationToken = default)
    {
        if (retentionDays <= 0) return 0;

        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);
        var records = await _db.InventoryMovements
            .Where(m => m.MovedAt < cutoff)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (records.Count == 0) return 0;

        _db.InventoryMovements.RemoveRange(records);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Logger.Information("Purged {Count} inventory movements older than {Days} days.", records.Count, retentionDays);
        return records.Count;
    }

    /// <summary>
    /// Purges completed or cancelled maintenance records older than the specified number of days.
    /// </summary>
    public async Task<int> PurgeMaintenanceRecordsAsync(int retentionDays, CancellationToken cancellationToken = default)
    {
        if (retentionDays <= 0) return 0;

        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);
        var records = await _db.MaintenanceRecords
            .Where(m => (m.Status == "Completed" || m.Status == "Cancelled") && m.CreatedAt < cutoff)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (records.Count == 0) return 0;

        _db.MaintenanceRecords.RemoveRange(records);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Logger.Information("Purged {Count} completed/cancelled maintenance records older than {Days} days.", records.Count, retentionDays);
        return records.Count;
    }

    /// <summary>
    /// Purges expired sessions older than the specified number of days.
    /// </summary>
    public async Task<int> PurgeSessionsAsync(int retentionDays, CancellationToken cancellationToken = default)
    {
        if (retentionDays <= 0) return 0;

        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        var records = await _db.Sessions
            .Where(s => !s.IsActive && s.ExpiresAt < cutoff)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (records.Count == 0) return 0;

        _db.Sessions.RemoveRange(records);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Logger.Information("Purged {Count} expired sessions older than {Days} days.", records.Count, retentionDays);
        return records.Count;
    }

    /// <summary>
    /// Returns counts of records that would be purged with the given retention settings.
    /// </summary>
    public async Task<RetentionPreview> PreviewPurgeAsync(RetentionSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var auditCutoff = DateTime.UtcNow.AddDays(-settings.AuditLogDays);
        var movementCutoff = DateTimeOffset.UtcNow.AddDays(-settings.MovementDays);
        var maintenanceCutoff = DateTimeOffset.UtcNow.AddDays(-settings.MaintenanceDays);
        var sessionCutoff = DateTime.UtcNow.AddDays(-settings.SessionDays);

        return new RetentionPreview
        {
            AuditLogCount = settings.AuditLogDays > 0
                ? await _db.AuditLog.CountAsync(a => a.Timestamp < auditCutoff, cancellationToken).ConfigureAwait(false)
                : 0,
            MovementCount = settings.MovementDays > 0
                ? await _db.InventoryMovements.CountAsync(m => m.MovedAt < movementCutoff, cancellationToken).ConfigureAwait(false)
                : 0,
            MaintenanceCount = settings.MaintenanceDays > 0
                ? await _db.MaintenanceRecords.CountAsync(m => (m.Status == "Completed" || m.Status == "Cancelled") && m.CreatedAt < maintenanceCutoff, cancellationToken).ConfigureAwait(false)
                : 0,
            SessionCount = settings.SessionDays > 0
                ? await _db.Sessions.CountAsync(s => !s.IsActive && s.ExpiresAt < sessionCutoff, cancellationToken).ConfigureAwait(false)
                : 0,
        };
    }

    /// <summary>
    /// Retrieves retention settings from the database.
    /// </summary>
    public async Task<RetentionSettings> GetRetentionSettingsAsync(CancellationToken cancellationToken = default)
    {
        var setting = await _db.AppSettings
            .FirstOrDefaultAsync(s => s.Key == SettingsKey, cancellationToken)
            .ConfigureAwait(false);

        if (setting is null || string.IsNullOrWhiteSpace(setting.ValueJson))
        {
            return new RetentionSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<RetentionSettings>(setting.ValueJson) ?? new RetentionSettings();
        }
        catch
        {
            return new RetentionSettings();
        }
    }

    /// <summary>
    /// Saves retention settings to the database.
    /// </summary>
    public async Task SaveRetentionSettingsAsync(RetentionSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var json = JsonSerializer.Serialize(settings);
        var existing = await _db.AppSettings
            .FirstOrDefaultAsync(s => s.Key == SettingsKey, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            _db.AppSettings.Add(new Entities.AppSettingEntity { Key = SettingsKey, ValueJson = json });
        }
        else
        {
            existing.ValueJson = json;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Configuration for data retention policies.
/// </summary>
public sealed class RetentionSettings
{
    public int AuditLogDays { get; set; } = 90;
    public int MovementDays { get; set; } = 180;
    public int MaintenanceDays { get; set; } = 365;
    public int SessionDays { get; set; } = 30;
}

/// <summary>
/// Preview of how many records would be purged.
/// </summary>
public sealed class RetentionPreview
{
    public int AuditLogCount { get; set; }
    public int MovementCount { get; set; }
    public int MaintenanceCount { get; set; }
    public int SessionCount { get; set; }

    public int Total => AuditLogCount + MovementCount + MaintenanceCount + SessionCount;
}
