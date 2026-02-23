#nullable enable
using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Mieruka.Core.Data.Entities;
using Serilog;

namespace Mieruka.Core.Data.Services;

/// <summary>
/// Resultado de uma importação de inventário.
/// </summary>
public sealed record ImportResult(int Categories, int Items, int Movements, int Maintenance);

/// <summary>
/// Importa dados de inventário a partir de Access (.accdb), SQL Server (.mdf)
/// ou SQL Server remoto para o banco SQLite principal.
/// </summary>
public sealed class InventoryImportService
{
    private static readonly ILogger Logger = Log.ForContext<InventoryImportService>();

    private readonly MierukaDbContext _targetContext;

    public InventoryImportService(MierukaDbContext targetContext)
    {
        _targetContext = targetContext ?? throw new ArgumentNullException(nameof(targetContext));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ACCESS (.accdb)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Importa dados de um arquivo Access (.accdb) para o banco principal.
    /// </summary>
    public async Task<ImportResult> ImportFromAccessAsync(string filePath, bool replaceAll, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Arquivo Access não encontrado.", filePath);

        Logger.Information("Importando inventário de Access: {FilePath} (replaceAll={ReplaceAll})", filePath, replaceAll);

        var connString = $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={filePath};";
        using var conn = new OleDbConnection(connString);
        await conn.OpenAsync(ct);

        var categories = await ReadAccessCategoriesAsync(conn, ct);
        var items = await ReadAccessItemsAsync(conn, ct);
        var movements = await ReadAccessMovementsAsync(conn, ct);
        var maintenance = await ReadAccessMaintenanceAsync(conn, ct);

        return await ImportDataAsync(categories, items, movements, maintenance, replaceAll, ct);
    }

    private static async Task<List<InventoryCategoryEntity>> ReadAccessCategoriesAsync(OleDbConnection conn, CancellationToken ct)
    {
        var result = new List<InventoryCategoryEntity>();
        if (!await TableExistsAsync(conn, "InventoryCategories", ct))
            return result;

        using var cmd = new OleDbCommand("SELECT Id, Name, Icon, Description, DisplayOrder, CustomFieldsJson, Color, CreatedAt, UpdatedAt FROM InventoryCategories", conn);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new InventoryCategoryEntity
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Icon = reader.IsDBNull(2) ? null : reader.GetString(2),
                Description = reader.IsDBNull(3) ? null : reader.GetString(3),
                DisplayOrder = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                CustomFieldsJson = reader.IsDBNull(5) ? null : reader.GetString(5),
                Color = reader.IsDBNull(6) ? null : reader.GetString(6),
                CreatedAt = reader.IsDBNull(7) ? DateTime.UtcNow : reader.GetDateTime(7),
                UpdatedAt = reader.IsDBNull(8) ? DateTime.UtcNow : reader.GetDateTime(8),
            });
        }
        return result;
    }

    private static async Task<List<InventoryItemEntity>> ReadAccessItemsAsync(OleDbConnection conn, CancellationToken ct)
    {
        var result = new List<InventoryItemEntity>();
        if (!await TableExistsAsync(conn, "InventoryItems", ct))
            return result;

        using var cmd = new OleDbCommand(
            @"SELECT Id, Name, CategoryId, Category, Description, SerialNumber, AssetTag,
                     Manufacturer, Model, Location, Status, Quantity, AssignedTo, Notes,
                     LinkedMonitorStableId, ItemNumber, MetadataJson, CustomFieldValuesJson,
                     UnitCostCents, AcquiredAt, WarrantyExpiresAt, CreatedAt, UpdatedAt
              FROM InventoryItems", conn);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new InventoryItemEntity
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                CategoryId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                Category = reader.GetString(3),
                Description = reader.IsDBNull(4) ? null : reader.GetString(4),
                SerialNumber = reader.IsDBNull(5) ? null : reader.GetString(5),
                AssetTag = reader.IsDBNull(6) ? null : reader.GetString(6),
                Manufacturer = reader.IsDBNull(7) ? null : reader.GetString(7),
                Model = reader.IsDBNull(8) ? null : reader.GetString(8),
                Location = reader.IsDBNull(9) ? null : reader.GetString(9),
                Status = reader.GetString(10),
                Quantity = reader.IsDBNull(11) ? 1 : reader.GetInt32(11),
                AssignedTo = reader.IsDBNull(12) ? null : reader.GetString(12),
                Notes = reader.IsDBNull(13) ? null : reader.GetString(13),
                LinkedMonitorStableId = reader.IsDBNull(14) ? null : reader.GetString(14),
                ItemNumber = reader.IsDBNull(15) ? 0 : reader.GetInt32(15),
                MetadataJson = reader.IsDBNull(16) ? null : reader.GetString(16),
                CustomFieldValuesJson = reader.IsDBNull(17) ? null : reader.GetString(17),
                UnitCostCents = reader.IsDBNull(18) ? null : Convert.ToInt64(reader.GetValue(18)),
                AcquiredAt = reader.IsDBNull(19) ? null : reader.GetDateTime(19),
                WarrantyExpiresAt = reader.IsDBNull(20) ? null : reader.GetDateTime(20),
                CreatedAt = reader.IsDBNull(21) ? DateTime.UtcNow : reader.GetDateTime(21),
                UpdatedAt = reader.IsDBNull(22) ? DateTime.UtcNow : reader.GetDateTime(22),
            });
        }
        return result;
    }

    private static async Task<List<InventoryMovementEntity>> ReadAccessMovementsAsync(OleDbConnection conn, CancellationToken ct)
    {
        var result = new List<InventoryMovementEntity>();
        if (!await TableExistsAsync(conn, "InventoryMovements", ct))
            return result;

        using var cmd = new OleDbCommand(
            @"SELECT Id, ItemId, MovementType, FromLocation, ToLocation, FromAssignee, ToAssignee, PerformedBy, Notes, MovedAt
              FROM InventoryMovements", conn);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new InventoryMovementEntity
            {
                Id = reader.GetInt32(0),
                ItemId = reader.GetInt32(1),
                MovementType = reader.GetString(2),
                FromLocation = reader.IsDBNull(3) ? null : reader.GetString(3),
                ToLocation = reader.IsDBNull(4) ? null : reader.GetString(4),
                FromAssignee = reader.IsDBNull(5) ? null : reader.GetString(5),
                ToAssignee = reader.IsDBNull(6) ? null : reader.GetString(6),
                PerformedBy = reader.IsDBNull(7) ? null : reader.GetString(7),
                Notes = reader.IsDBNull(8) ? null : reader.GetString(8),
                MovedAt = reader.IsDBNull(9) ? DateTime.UtcNow : reader.GetDateTime(9),
            });
        }
        return result;
    }

    private static async Task<List<MaintenanceRecordEntity>> ReadAccessMaintenanceAsync(OleDbConnection conn, CancellationToken ct)
    {
        var result = new List<MaintenanceRecordEntity>();
        if (!await TableExistsAsync(conn, "MaintenanceRecords", ct))
            return result;

        using var cmd = new OleDbCommand(
            @"SELECT Id, ItemId, MaintenanceType, Description, PerformedBy, CostCents, Status, ScheduledAt, CompletedAt, Notes, CreatedAt
              FROM MaintenanceRecords", conn);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new MaintenanceRecordEntity
            {
                Id = reader.GetInt32(0),
                ItemId = reader.GetInt32(1),
                MaintenanceType = reader.GetString(2),
                Description = reader.GetString(3),
                PerformedBy = reader.IsDBNull(4) ? null : reader.GetString(4),
                CostCents = reader.IsDBNull(5) ? null : Convert.ToInt64(reader.GetValue(5)),
                Status = reader.GetString(6),
                ScheduledAt = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                CompletedAt = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                Notes = reader.IsDBNull(9) ? null : reader.GetString(9),
                CreatedAt = reader.IsDBNull(10) ? DateTime.UtcNow : reader.GetDateTime(10),
            });
        }
        return result;
    }

    private static async Task<bool> TableExistsAsync(OleDbConnection conn, string tableName, CancellationToken ct)
    {
        try
        {
            using var cmd = new OleDbCommand($"SELECT TOP 1 1 FROM [{tableName}]", conn);
            await cmd.ExecuteScalarAsync(ct);
            return true;
        }
        catch (OleDbException)
        {
            return false;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SQL SERVER (.mdf)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Importa dados de um arquivo SQL Server (.mdf) para o banco principal.
    /// Requer SQL Server LocalDB instalado.
    /// </summary>
    public async Task<ImportResult> ImportFromSqlServerAsync(string filePath, bool replaceAll, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Arquivo MDF não encontrado.", filePath);

        Logger.Information("Importando inventário de SQL Server MDF: {FilePath} (replaceAll={ReplaceAll})", filePath, replaceAll);

        var dbName = $"MierukaImport_{Guid.NewGuid():N}";
        var masterConnString = @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;Connection Timeout=10;";

        // Anexar o arquivo .mdf com um nome temporário.
        using (var masterConn = new SqlConnection(masterConnString))
        {
            await masterConn.OpenAsync(ct);
            var attachSql = $"CREATE DATABASE [{dbName}] ON (FILENAME = N'{filePath}') FOR ATTACH_REBUILD_LOG";
            using var cmd = new SqlCommand(attachSql, masterConn);
            cmd.CommandTimeout = 30;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        try
        {
            var sourceConnString = @$"Server=(localdb)\MSSQLLocalDB;Database={dbName};Integrated Security=true;Connection Timeout=10;";
            var options = new DbContextOptionsBuilder<MierukaDbContext>()
                .UseSqlServer(sourceConnString)
                .Options;

            List<InventoryCategoryEntity> categories;
            List<InventoryItemEntity> items;
            List<InventoryMovementEntity> movements;
            List<MaintenanceRecordEntity> maintenance;

            using (var sourceCtx = new MierukaDbContext(options))
            {
                categories = await sourceCtx.InventoryCategories.AsNoTracking().ToListAsync(ct);
                items = await sourceCtx.InventoryItems.AsNoTracking().ToListAsync(ct);
                movements = await sourceCtx.InventoryMovements.AsNoTracking().ToListAsync(ct);
                maintenance = await sourceCtx.MaintenanceRecords.AsNoTracking().ToListAsync(ct);
            }

            return await ImportDataAsync(categories, items, movements, maintenance, replaceAll, ct);
        }
        finally
        {
            // Desanexar o banco temporário.
            DetachDatabase(dbName);
        }
    }

    private static void DetachDatabase(string dbName)
    {
        try
        {
            using var conn = new SqlConnection(@"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;Connection Timeout=5;");
            conn.Open();

            using (var cmd = new SqlCommand($"ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE", conn))
            {
                try { cmd.ExecuteNonQuery(); } catch { /* pode não existir */ }
            }

            using (var detachCmd = new SqlCommand($"EXEC sp_detach_db @dbname = N'{dbName}'", conn))
            {
                try { detachCmd.ExecuteNonQuery(); } catch { /* pode não existir */ }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Falha ao desanexar banco temporário {DbName}.", dbName);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SQL SERVER REMOTO (conexão direta)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Importa dados de um SQL Server remoto (conexão direta) para o banco principal.
    /// </summary>
    public async Task<ImportResult> ImportFromRemoteSqlServerAsync(
        string connectionString, bool replaceAll, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(connectionString);
        Logger.Information("Importando inventário de SQL Server remoto (replaceAll={ReplaceAll})", replaceAll);

        var options = new DbContextOptionsBuilder<MierukaDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        List<InventoryCategoryEntity> categories;
        List<InventoryItemEntity> items;
        List<InventoryMovementEntity> movements;
        List<MaintenanceRecordEntity> maintenance;

        using (var sourceCtx = new MierukaDbContext(options))
        {
            categories = await sourceCtx.InventoryCategories.AsNoTracking().ToListAsync(ct);
            items = await sourceCtx.InventoryItems.AsNoTracking().ToListAsync(ct);
            movements = await sourceCtx.InventoryMovements.AsNoTracking().ToListAsync(ct);
            maintenance = await sourceCtx.MaintenanceRecords.AsNoTracking().ToListAsync(ct);
        }

        return await ImportDataAsync(categories, items, movements, maintenance, replaceAll, ct);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // IMPORTAÇÃO COMPARTILHADA
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task<ImportResult> ImportDataAsync(
        List<InventoryCategoryEntity> categories,
        List<InventoryItemEntity> items,
        List<InventoryMovementEntity> movements,
        List<MaintenanceRecordEntity> maintenance,
        bool replaceAll,
        CancellationToken ct)
    {
        using var transaction = await _targetContext.Database.BeginTransactionAsync(ct);
        try
        {
            if (replaceAll)
            {
                await ClearInventoryTablesAsync(ct);
                await InsertWithOriginalIdsAsync(categories, items, movements, maintenance, ct);
            }
            else
            {
                await InsertWithNewIdsAsync(categories, items, movements, maintenance, ct);
            }

            await transaction.CommitAsync(ct);

            var result = new ImportResult(categories.Count, items.Count, movements.Count, maintenance.Count);
            Logger.Information(
                "Importação concluída: {Categories} categorias, {Items} itens, {Movements} movimentações, {Maintenance} manutenções",
                result.Categories, result.Items, result.Movements, result.Maintenance);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    private async Task ClearInventoryTablesAsync(CancellationToken ct)
    {
        // Ordem: tabelas dependentes primeiro (FK cascade, mas sendo explícito).
        await _targetContext.Database.ExecuteSqlRawAsync("DELETE FROM MaintenanceRecords", ct);
        await _targetContext.Database.ExecuteSqlRawAsync("DELETE FROM InventoryMovements", ct);
        await _targetContext.Database.ExecuteSqlRawAsync("DELETE FROM InventoryItems", ct);
        await _targetContext.Database.ExecuteSqlRawAsync("DELETE FROM InventoryCategories", ct);
    }

    /// <summary>
    /// Modo Substituir: insere com IDs originais preservados.
    /// </summary>
    private async Task InsertWithOriginalIdsAsync(
        List<InventoryCategoryEntity> categories,
        List<InventoryItemEntity> items,
        List<InventoryMovementEntity> movements,
        List<MaintenanceRecordEntity> maintenance,
        CancellationToken ct)
    {
        var autoDetect = _targetContext.ChangeTracker.AutoDetectChangesEnabled;
        _targetContext.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            if (categories.Count > 0)
            {
                _targetContext.InventoryCategories.AddRange(categories);
                await _targetContext.SaveChangesAsync(ct);
            }

            if (items.Count > 0)
            {
                _targetContext.InventoryItems.AddRange(items);
                await _targetContext.SaveChangesAsync(ct);
            }

            if (movements.Count > 0)
            {
                _targetContext.InventoryMovements.AddRange(movements);
                await _targetContext.SaveChangesAsync(ct);
            }

            if (maintenance.Count > 0)
            {
                _targetContext.MaintenanceRecords.AddRange(maintenance);
                await _targetContext.SaveChangesAsync(ct);
            }
        }
        finally
        {
            _targetContext.ChangeTracker.AutoDetectChangesEnabled = autoDetect;
        }
    }

    /// <summary>
    /// Modo Adicionar: reseta IDs para 0 (auto-generated) e remapeia FKs.
    /// Categorias com nomes duplicados reutilizam a existente.
    /// Usa AddRange para minimizar round-trips ao banco.
    /// </summary>
    private async Task InsertWithNewIdsAsync(
        List<InventoryCategoryEntity> categories,
        List<InventoryItemEntity> items,
        List<InventoryMovementEntity> movements,
        List<MaintenanceRecordEntity> maintenance,
        CancellationToken ct)
    {
        // 1. Categorias: resolver existentes e inserir novas em batch.
        var categoryMap = new Dictionary<int, int>();
        var existingCats = await _targetContext.InventoryCategories
            .AsNoTracking()
            .ToDictionaryAsync(c => c.Name, c => c.Id, ct);

        var newCats = new List<(int oldId, InventoryCategoryEntity entity)>();
        foreach (var cat in categories)
        {
            if (existingCats.TryGetValue(cat.Name, out var existingId))
            {
                categoryMap[cat.Id] = existingId;
            }
            else
            {
                newCats.Add((cat.Id, cat));
                cat.Id = 0;
            }
        }

        var autoDetect = _targetContext.ChangeTracker.AutoDetectChangesEnabled;
        _targetContext.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            if (newCats.Count > 0)
            {
                _targetContext.InventoryCategories.AddRange(newCats.Select(c => c.entity));
                await _targetContext.SaveChangesAsync(ct);
                foreach (var (oldId, entity) in newCats)
                {
                    categoryMap[oldId] = entity.Id;
                }
            }

            // 2. Itens: remapear CategoryId e inserir em batch.
            var oldItemIds = new List<(int oldId, InventoryItemEntity entity)>();
            foreach (var item in items)
            {
                oldItemIds.Add((item.Id, item));
                item.Id = 0;
                if (item.CategoryId.HasValue && categoryMap.TryGetValue(item.CategoryId.Value, out var newCatId))
                {
                    item.CategoryId = newCatId;
                }
                else
                {
                    item.CategoryId = null;
                }
            }

            if (items.Count > 0)
            {
                _targetContext.InventoryItems.AddRange(items);
                await _targetContext.SaveChangesAsync(ct);
            }

            var itemMap = new Dictionary<int, int>();
            foreach (var (oldId, entity) in oldItemIds)
            {
                itemMap[oldId] = entity.Id;
            }

            // 3. Movimentações: remapear ItemId e inserir em batch.
            var validMovements = new List<InventoryMovementEntity>();
            foreach (var mov in movements)
            {
                if (itemMap.TryGetValue(mov.ItemId, out var newItemId))
                {
                    mov.Id = 0;
                    mov.ItemId = newItemId;
                    validMovements.Add(mov);
                }
            }
            if (validMovements.Count > 0)
            {
                _targetContext.InventoryMovements.AddRange(validMovements);
                await _targetContext.SaveChangesAsync(ct);
            }

            // 4. Manutenções: remapear ItemId e inserir em batch.
            var validMaintenance = new List<MaintenanceRecordEntity>();
            foreach (var rec in maintenance)
            {
                if (itemMap.TryGetValue(rec.ItemId, out var newItemId))
                {
                    rec.Id = 0;
                    rec.ItemId = newItemId;
                    validMaintenance.Add(rec);
                }
            }
            if (validMaintenance.Count > 0)
            {
                _targetContext.MaintenanceRecords.AddRange(validMaintenance);
                await _targetContext.SaveChangesAsync(ct);
            }
        }
        finally
        {
            _targetContext.ChangeTracker.AutoDetectChangesEnabled = autoDetect;
        }
    }
}
