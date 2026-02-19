using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Mieruka.Core.Data.Entities;
using Mieruka.Core.Models;
using Serilog;

namespace Mieruka.Core.Data.Services;

/// <summary>
/// Manages configuration snapshots for versioning and rollback.
/// </summary>
public sealed class ConfigSnapshotService
{
    private static readonly ILogger Logger = Log.ForContext<ConfigSnapshotService>();

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly MierukaDbContext _db;

    public ConfigSnapshotService(MierukaDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <summary>
    /// Creates a snapshot of the current configuration.
    /// </summary>
    public async Task CreateSnapshotAsync(GeneralConfig config, string label, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        var json = JsonSerializer.Serialize(config, SerializerOptions);
        var entity = new ConfigSnapshotEntity
        {
            Label = label ?? "Snapshot",
            ConfigJson = json,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _db.ConfigSnapshots.Add(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        Logger.Information("Configuration snapshot created: {Label} (Id={Id})", entity.Label, entity.Id);
    }

    /// <summary>
    /// Returns all snapshots ordered by creation date (newest first).
    /// </summary>
    public async Task<IReadOnlyList<ConfigSnapshotEntity>> GetAllSnapshotsAsync(CancellationToken cancellationToken = default)
    {
        return await _db.ConfigSnapshots
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Restores a configuration from a snapshot.
    /// </summary>
    public async Task<GeneralConfig?> RestoreSnapshotAsync(int snapshotId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.ConfigSnapshots
            .FirstOrDefaultAsync(s => s.Id == snapshotId, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return null;
        }

        try
        {
            var config = JsonSerializer.Deserialize<GeneralConfig>(entity.ConfigJson, SerializerOptions);
            Logger.Information("Configuration restored from snapshot {Id}: {Label}", entity.Id, entity.Label);
            return config;
        }
        catch (JsonException ex)
        {
            Logger.Warning(ex, "Failed to deserialize snapshot {Id}", snapshotId);
            return null;
        }
    }

    /// <summary>
    /// Deletes a specific snapshot.
    /// </summary>
    public async Task DeleteSnapshotAsync(int snapshotId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.ConfigSnapshots
            .FirstOrDefaultAsync(s => s.Id == snapshotId, cancellationToken)
            .ConfigureAwait(false);

        if (entity is not null)
        {
            _db.ConfigSnapshots.Remove(entity);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Removes old snapshots keeping only the most recent ones.
    /// </summary>
    public async Task PruneOldSnapshotsAsync(int maxCount = 50, CancellationToken cancellationToken = default)
    {
        var total = await _db.ConfigSnapshots.CountAsync(cancellationToken).ConfigureAwait(false);
        if (total <= maxCount)
        {
            return;
        }

        var toRemove = await _db.ConfigSnapshots
            .OrderByDescending(s => s.CreatedAt)
            .Skip(maxCount)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (toRemove.Count > 0)
        {
            _db.ConfigSnapshots.RemoveRange(toRemove);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            Logger.Information("Pruned {Count} old configuration snapshots.", toRemove.Count);
        }
    }
}
