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
/// Exporta dados do inventário para Access (.accdb), SQL Server (.mdf) ou SQL Server remoto.
/// </summary>
public sealed class InventoryExportService
{
    private static readonly ILogger Logger = Log.ForContext<InventoryExportService>();

    private readonly MierukaDbContext _sourceContext;

    public InventoryExportService(MierukaDbContext sourceContext)
    {
        _sourceContext = sourceContext ?? throw new ArgumentNullException(nameof(sourceContext));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ACCESS (.accdb)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifica se o driver ACE OLEDB está disponível na máquina.
    /// </summary>
    public static bool IsAccessDriverAvailable()
    {
        try
        {
            var factory = System.Data.Common.DbProviderFactories.GetFactory("System.Data.OleDb");
            return factory is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Exporta todos os dados do inventário para um arquivo Access (.accdb).
    /// </summary>
    public async Task ExportToAccessAsync(string filePath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        Logger.Information("Exportando inventário para Access: {FilePath}", filePath);

        if (File.Exists(filePath))
            File.Delete(filePath);

        // Criar o arquivo .accdb vazio via ADOX (COM interop) ou via catálogo.
        CreateEmptyAccessDatabase(filePath);

        var connString = $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={filePath};";
        using var conn = new OleDbConnection(connString);
        await conn.OpenAsync(ct);

        // Criar tabelas.
        await CreateAccessTablesAsync(conn, ct);

        // Carregar dados da origem.
        var items = await _sourceContext.InventoryItems.AsNoTracking().ToListAsync(ct);
        var categories = await _sourceContext.InventoryCategories.AsNoTracking().ToListAsync(ct);
        var movements = await _sourceContext.InventoryMovements.AsNoTracking().ToListAsync(ct);
        var maintenance = await _sourceContext.MaintenanceRecords.AsNoTracking().ToListAsync(ct);

        // Inserir dados dentro de uma transação para reduzir overhead de auto-commit.
        using var transaction = conn.BeginTransaction();
        await InsertAccessCategoriesAsync(conn, transaction, categories, ct);
        await InsertAccessItemsAsync(conn, transaction, items, ct);
        await InsertAccessMovementsAsync(conn, transaction, movements, ct);
        await InsertAccessMaintenanceAsync(conn, transaction, maintenance, ct);
        transaction.Commit();

        Logger.Information("Exportação Access concluída: {Items} itens, {Categories} categorias, {Movements} movimentações, {Maintenance} manutenções",
            items.Count, categories.Count, movements.Count, maintenance.Count);
    }

    private static void CreateEmptyAccessDatabase(string filePath)
    {
        // Usar ADOX via COM para criar o arquivo .accdb vazio.
        var catalogType = Type.GetTypeFromProgID("ADOX.Catalog");
        if (catalogType is null)
        {
            throw new InvalidOperationException(
                "O componente ADOX não está disponível. Instale o Microsoft Access Database Engine:\n" +
                "https://www.microsoft.com/pt-br/download/details.aspx?id=54920");
        }

        dynamic catalog = Activator.CreateInstance(catalogType)!;
        try
        {
            catalog.Create($"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={filePath};");
        }
        finally
        {
            var connection = catalog.ActiveConnection;
            if (connection is not null)
            {
                connection.Close();
            }
            System.Runtime.InteropServices.Marshal.ReleaseComObject(catalog);
        }
    }

    private static async Task CreateAccessTablesAsync(OleDbConnection conn, CancellationToken ct)
    {
        var createStatements = new[]
        {
            @"CREATE TABLE InventoryCategories (
                Id AUTOINCREMENT PRIMARY KEY,
                Name VARCHAR(100) NOT NULL,
                Icon VARCHAR(50),
                Description VARCHAR(500),
                DisplayOrder INTEGER,
                CustomFieldsJson MEMO,
                Color VARCHAR(20),
                CreatedAt DATETIME,
                UpdatedAt DATETIME
            )",
            @"CREATE TABLE InventoryItems (
                Id AUTOINCREMENT PRIMARY KEY,
                Name VARCHAR(200) NOT NULL,
                CategoryId INTEGER,
                Category VARCHAR(100) NOT NULL,
                Description MEMO,
                SerialNumber VARCHAR(200),
                AssetTag VARCHAR(100),
                Manufacturer VARCHAR(200),
                Model VARCHAR(200),
                Location VARCHAR(300),
                Status VARCHAR(50) NOT NULL,
                Quantity INTEGER,
                AssignedTo VARCHAR(200),
                Notes MEMO,
                LinkedMonitorStableId VARCHAR(200),
                ItemNumber INTEGER,
                MetadataJson MEMO,
                CustomFieldValuesJson MEMO,
                UnitCostCents LONG,
                AcquiredAt DATETIME,
                WarrantyExpiresAt DATETIME,
                CreatedAt DATETIME,
                UpdatedAt DATETIME
            )",
            @"CREATE TABLE InventoryMovements (
                Id AUTOINCREMENT PRIMARY KEY,
                ItemId INTEGER NOT NULL,
                MovementType VARCHAR(50) NOT NULL,
                FromLocation VARCHAR(300),
                ToLocation VARCHAR(300),
                FromAssignee VARCHAR(200),
                ToAssignee VARCHAR(200),
                PerformedBy VARCHAR(200),
                Notes MEMO,
                MovedAt DATETIME
            )",
            @"CREATE TABLE MaintenanceRecords (
                Id AUTOINCREMENT PRIMARY KEY,
                ItemId INTEGER NOT NULL,
                MaintenanceType VARCHAR(50) NOT NULL,
                Description MEMO NOT NULL,
                PerformedBy VARCHAR(200),
                CostCents LONG,
                Status VARCHAR(50) NOT NULL,
                ScheduledAt DATETIME,
                CompletedAt DATETIME,
                Notes MEMO,
                CreatedAt DATETIME
            )",
        };

        foreach (var sql in createStatements)
        {
            using var cmd = new OleDbCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task InsertAccessCategoriesAsync(OleDbConnection conn, OleDbTransaction transaction, List<InventoryCategoryEntity> categories, CancellationToken ct)
    {
        const string sql = @"INSERT INTO InventoryCategories
            (Name, Icon, Description, DisplayOrder, CustomFieldsJson, Color, CreatedAt, UpdatedAt)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?)";

        foreach (var cat in categories)
        {
            using var cmd = new OleDbCommand(sql, conn, transaction);
            cmd.Parameters.AddWithValue("@Name", cat.Name);
            cmd.Parameters.AddWithValue("@Icon", (object?)cat.Icon ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Description", (object?)cat.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@DisplayOrder", cat.DisplayOrder);
            cmd.Parameters.AddWithValue("@CustomFieldsJson", (object?)cat.CustomFieldsJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Color", (object?)cat.Color ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CreatedAt", cat.CreatedAt);
            cmd.Parameters.AddWithValue("@UpdatedAt", cat.UpdatedAt);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task InsertAccessItemsAsync(OleDbConnection conn, OleDbTransaction transaction, List<InventoryItemEntity> items, CancellationToken ct)
    {
        const string sql = @"INSERT INTO InventoryItems
            (Name, CategoryId, Category, Description, SerialNumber, AssetTag,
             Manufacturer, Model, Location, Status, Quantity, AssignedTo, Notes,
             LinkedMonitorStableId, ItemNumber, MetadataJson, CustomFieldValuesJson,
             UnitCostCents, AcquiredAt, WarrantyExpiresAt, CreatedAt, UpdatedAt)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)";

        foreach (var item in items)
        {
            using var cmd = new OleDbCommand(sql, conn, transaction);
            cmd.Parameters.AddWithValue("@Name", item.Name);
            cmd.Parameters.AddWithValue("@CategoryId", (object?)item.CategoryId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Category", item.Category);
            cmd.Parameters.AddWithValue("@Description", (object?)item.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SerialNumber", (object?)item.SerialNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@AssetTag", (object?)item.AssetTag ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Manufacturer", (object?)item.Manufacturer ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Model", (object?)item.Model ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Location", (object?)item.Location ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Status", item.Status);
            cmd.Parameters.AddWithValue("@Quantity", item.Quantity);
            cmd.Parameters.AddWithValue("@AssignedTo", (object?)item.AssignedTo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Notes", (object?)item.Notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@LinkedMonitorStableId", (object?)item.LinkedMonitorStableId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ItemNumber", item.ItemNumber);
            cmd.Parameters.AddWithValue("@MetadataJson", (object?)item.MetadataJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CustomFieldValuesJson", (object?)item.CustomFieldValuesJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@UnitCostCents", (object?)item.UnitCostCents ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@AcquiredAt", (object?)item.AcquiredAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@WarrantyExpiresAt", (object?)item.WarrantyExpiresAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CreatedAt", item.CreatedAt);
            cmd.Parameters.AddWithValue("@UpdatedAt", item.UpdatedAt);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task InsertAccessMovementsAsync(OleDbConnection conn, OleDbTransaction transaction, List<InventoryMovementEntity> movements, CancellationToken ct)
    {
        const string sql = @"INSERT INTO InventoryMovements
            (ItemId, MovementType, FromLocation, ToLocation, FromAssignee, ToAssignee, PerformedBy, Notes, MovedAt)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)";

        foreach (var mov in movements)
        {
            using var cmd = new OleDbCommand(sql, conn, transaction);
            cmd.Parameters.AddWithValue("@ItemId", mov.ItemId);
            cmd.Parameters.AddWithValue("@MovementType", mov.MovementType);
            cmd.Parameters.AddWithValue("@FromLocation", (object?)mov.FromLocation ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ToLocation", (object?)mov.ToLocation ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@FromAssignee", (object?)mov.FromAssignee ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ToAssignee", (object?)mov.ToAssignee ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@PerformedBy", (object?)mov.PerformedBy ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Notes", (object?)mov.Notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@MovedAt", mov.MovedAt);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task InsertAccessMaintenanceAsync(OleDbConnection conn, OleDbTransaction transaction, List<MaintenanceRecordEntity> records, CancellationToken ct)
    {
        const string sql = @"INSERT INTO MaintenanceRecords
            (ItemId, MaintenanceType, Description, PerformedBy, CostCents, Status, ScheduledAt, CompletedAt, Notes, CreatedAt)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)";

        foreach (var rec in records)
        {
            using var cmd = new OleDbCommand(sql, conn, transaction);
            cmd.Parameters.AddWithValue("@ItemId", rec.ItemId);
            cmd.Parameters.AddWithValue("@MaintenanceType", rec.MaintenanceType);
            cmd.Parameters.AddWithValue("@Description", rec.Description);
            cmd.Parameters.AddWithValue("@PerformedBy", (object?)rec.PerformedBy ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CostCents", (object?)rec.CostCents ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Status", rec.Status);
            cmd.Parameters.AddWithValue("@ScheduledAt", (object?)rec.ScheduledAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CompletedAt", (object?)rec.CompletedAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Notes", (object?)rec.Notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CreatedAt", rec.CreatedAt);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SQL SERVER (.mdf)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifica se SQL Server LocalDB está disponível.
    /// </summary>
    public static bool IsSqlServerLocalDbAvailable()
    {
        try
        {
            using var conn = new SqlConnection(@"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;Connection Timeout=3;");
            conn.Open();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Exporta todos os dados do inventário para um arquivo SQL Server (.mdf).
    /// Requer SQL Server LocalDB instalado.
    /// </summary>
    public async Task ExportToSqlServerAsync(string filePath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        Logger.Information("Exportando inventário para SQL Server MDF: {FilePath}", filePath);

        // Remover arquivos existentes.
        var logPath = Path.ChangeExtension(filePath, ".ldf");
        DetachIfExists(filePath);
        if (File.Exists(filePath)) File.Delete(filePath);
        if (File.Exists(logPath)) File.Delete(logPath);

        var dbName = Path.GetFileNameWithoutExtension(filePath);
        var masterConnString = @$"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;Connection Timeout=10;";

        // Criar banco de dados no caminho especificado.
        using (var masterConn = new SqlConnection(masterConnString))
        {
            await masterConn.OpenAsync(ct);
            var createSql = $"CREATE DATABASE [{dbName}] ON PRIMARY (NAME=N'{dbName}', FILENAME=N'{filePath}')";
            using var cmd = new SqlCommand(createSql, masterConn);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Conectar ao novo banco e criar schema via EF Core.
        var targetConnString = @$"Server=(localdb)\MSSQLLocalDB;AttachDbFilename={filePath};Database={dbName};Integrated Security=true;";
        var options = new DbContextOptionsBuilder<MierukaDbContext>()
            .UseSqlServer(targetConnString)
            .Options;

        using (var targetCtx = new MierukaDbContext(options))
        {
            // Criar tabelas usando o modelo EF Core.
            await targetCtx.Database.EnsureCreatedAsync(ct);

            // Carregar dados da origem.
            var items = await _sourceContext.InventoryItems.AsNoTracking().ToListAsync(ct);
            var categories = await _sourceContext.InventoryCategories.AsNoTracking().ToListAsync(ct);
            var movements = await _sourceContext.InventoryMovements.AsNoTracking().ToListAsync(ct);
            var maintenance = await _sourceContext.MaintenanceRecords.AsNoTracking().ToListAsync(ct);

            // Desabilitar detecção automática de mudanças durante bulk insert.
            var autoDetect = targetCtx.ChangeTracker.AutoDetectChangesEnabled;
            targetCtx.ChangeTracker.AutoDetectChangesEnabled = false;
            try
            {
                // Inserir categorias (com IDENTITY_INSERT).
                if (categories.Count > 0)
                {
                    await targetCtx.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT InventoryCategories ON", ct);
                    targetCtx.InventoryCategories.AddRange(categories);
                    await targetCtx.SaveChangesAsync(ct);
                    await targetCtx.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT InventoryCategories OFF", ct);
                }

                // Inserir itens (com IDENTITY_INSERT).
                if (items.Count > 0)
                {
                    await targetCtx.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT InventoryItems ON", ct);
                    targetCtx.InventoryItems.AddRange(items);
                    await targetCtx.SaveChangesAsync(ct);
                    await targetCtx.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT InventoryItems OFF", ct);
                }

                // Inserir movimentações.
                if (movements.Count > 0)
                {
                    await targetCtx.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT InventoryMovements ON", ct);
                    targetCtx.InventoryMovements.AddRange(movements);
                    await targetCtx.SaveChangesAsync(ct);
                    await targetCtx.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT InventoryMovements OFF", ct);
                }

                // Inserir manutenções.
                if (maintenance.Count > 0)
                {
                    await targetCtx.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT MaintenanceRecords ON", ct);
                    targetCtx.MaintenanceRecords.AddRange(maintenance);
                    await targetCtx.SaveChangesAsync(ct);
                    await targetCtx.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT MaintenanceRecords OFF", ct);
                }
            }
            finally
            {
                targetCtx.ChangeTracker.AutoDetectChangesEnabled = autoDetect;
            }

            Logger.Information("Exportação SQL Server concluída: {Items} itens, {Categories} categorias, {Movements} movimentações, {Maintenance} manutenções",
                items.Count, categories.Count, movements.Count, maintenance.Count);
        }

        // Desanexar o banco para que o .mdf fique independente.
        DetachDatabase(dbName);
    }

    private static void DetachIfExists(string filePath)
    {
        try
        {
            var dbName = Path.GetFileNameWithoutExtension(filePath);
            DetachDatabase(dbName);
        }
        catch
        {
            // Ignorar se não estava anexado.
        }
    }

    private static void DetachDatabase(string dbName)
    {
        using var conn = new SqlConnection(@"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;Connection Timeout=5;");
        conn.Open();

        // Forçar desconexão de usuários.
        using (var cmd = new SqlCommand($"ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE", conn))
        {
            try { cmd.ExecuteNonQuery(); } catch { /* pode não existir */ }
        }

        using (var detachCmd = new SqlCommand($"EXEC sp_detach_db @dbname = N'{dbName}'", conn))
        {
            try { detachCmd.ExecuteNonQuery(); } catch { /* pode não existir */ }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SQL SERVER REMOTO (conexão direta)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Exporta todos os dados do inventário para um SQL Server remoto (conexão direta).
    /// Cria o schema se não existir. Se <paramref name="replaceAll"/> for true,
    /// limpa as tabelas antes de inserir.
    /// </summary>
    public async Task ExportToRemoteSqlServerAsync(
        string connectionString, bool replaceAll, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(connectionString);
        Logger.Information("Exportando inventário para SQL Server remoto (replaceAll={ReplaceAll})", replaceAll);

        var options = new DbContextOptionsBuilder<MierukaDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        using var targetCtx = new MierukaDbContext(options);

        // Criar schema se não existir.
        await targetCtx.Database.EnsureCreatedAsync(ct);

        // Carregar dados da origem.
        var items = await _sourceContext.InventoryItems.AsNoTracking().ToListAsync(ct);
        var categories = await _sourceContext.InventoryCategories.AsNoTracking().ToListAsync(ct);
        var movements = await _sourceContext.InventoryMovements.AsNoTracking().ToListAsync(ct);
        var maintenance = await _sourceContext.MaintenanceRecords.AsNoTracking().ToListAsync(ct);

        if (replaceAll)
        {
            // Limpar tabelas na ordem FK (dependentes primeiro).
            await targetCtx.Database.ExecuteSqlRawAsync("DELETE FROM MaintenanceRecords", ct);
            await targetCtx.Database.ExecuteSqlRawAsync("DELETE FROM InventoryMovements", ct);
            await targetCtx.Database.ExecuteSqlRawAsync("DELETE FROM InventoryItems", ct);
            await targetCtx.Database.ExecuteSqlRawAsync("DELETE FROM InventoryCategories", ct);
        }

        var autoDetect = targetCtx.ChangeTracker.AutoDetectChangesEnabled;
        targetCtx.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            if (categories.Count > 0)
            {
                await targetCtx.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT InventoryCategories ON", ct);
                targetCtx.InventoryCategories.AddRange(categories);
                await targetCtx.SaveChangesAsync(ct);
                await targetCtx.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT InventoryCategories OFF", ct);
            }

            if (items.Count > 0)
            {
                await targetCtx.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT InventoryItems ON", ct);
                targetCtx.InventoryItems.AddRange(items);
                await targetCtx.SaveChangesAsync(ct);
                await targetCtx.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT InventoryItems OFF", ct);
            }

            if (movements.Count > 0)
            {
                await targetCtx.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT InventoryMovements ON", ct);
                targetCtx.InventoryMovements.AddRange(movements);
                await targetCtx.SaveChangesAsync(ct);
                await targetCtx.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT InventoryMovements OFF", ct);
            }

            if (maintenance.Count > 0)
            {
                await targetCtx.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT MaintenanceRecords ON", ct);
                targetCtx.MaintenanceRecords.AddRange(maintenance);
                await targetCtx.SaveChangesAsync(ct);
                await targetCtx.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT MaintenanceRecords OFF", ct);
            }
        }
        finally
        {
            targetCtx.ChangeTracker.AutoDetectChangesEnabled = autoDetect;
        }

        Logger.Information(
            "Exportação SQL Server remoto concluída: {Items} itens, {Categories} categorias, {Movements} movimentações, {Maintenance} manutenções",
            items.Count, categories.Count, movements.Count, maintenance.Count);
    }
}
