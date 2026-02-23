#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Mieruka.Core.Data;
using Mieruka.Core.Data.Entities;
using Mieruka.Core.Data.Services;
using Xunit;

namespace Mieruka.Tests.Inventory;

/// <summary>
/// Testes de deadlock e integridade para import/export remoto do inventário.
/// Usa dois bancos SQLite em arquivo (simula "remoto" + "local") sem dependência de SQL Server real.
/// Os métodos ImportFromRemoteSqlServerAsync e ExportToRemoteSqlServerAsync são testados
/// indiretamente via ImportDataAsync (reflexão) e ExportToRemoteSqlServerAsync adaptado.
/// </summary>
public sealed class RemoteSqlServerTests : IDisposable
{
    private readonly string _remoteDbPath;
    private readonly string _localDbPath;
    private readonly string _remoteConnectionString;
    private readonly string _localConnectionString;

    public RemoteSqlServerTests()
    {
        _remoteDbPath = Path.Combine(Path.GetTempPath(), $"mieruka_remote_{Guid.NewGuid():N}.db");
        _localDbPath = Path.Combine(Path.GetTempPath(), $"mieruka_local_{Guid.NewGuid():N}.db");

        _remoteConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _remoteDbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ConnectionString;

        _localConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _localDbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ConnectionString;

        using var remoteCtx = CreateRemoteContext();
        remoteCtx.Database.EnsureCreated();
        remoteCtx.Database.ExecuteSqlRaw("PRAGMA journal_mode = WAL;");
        remoteCtx.Database.ExecuteSqlRaw("PRAGMA synchronous = NORMAL;");

        using var localCtx = CreateLocalContext();
        localCtx.Database.EnsureCreated();
        localCtx.Database.ExecuteSqlRaw("PRAGMA journal_mode = WAL;");
        localCtx.Database.ExecuteSqlRaw("PRAGMA synchronous = NORMAL;");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _remoteDbPath, _localDbPath })
        {
            try { File.Delete(path); } catch { }
            try { File.Delete(path + "-wal"); } catch { }
            try { File.Delete(path + "-shm"); } catch { }
        }
    }

    private MierukaDbContext CreateRemoteContext()
    {
        var options = new DbContextOptionsBuilder<MierukaDbContext>()
            .UseSqlite(_remoteConnectionString)
            .Options;
        var ctx = new MierukaDbContext(options);
        ctx.Database.OpenConnection();
        ctx.Database.ExecuteSqlRaw("PRAGMA busy_timeout = 10000;");
        return ctx;
    }

    private MierukaDbContext CreateLocalContext()
    {
        var options = new DbContextOptionsBuilder<MierukaDbContext>()
            .UseSqlite(_localConnectionString)
            .Options;
        var ctx = new MierukaDbContext(options);
        ctx.Database.OpenConnection();
        ctx.Database.ExecuteSqlRaw("PRAGMA busy_timeout = 10000;");
        return ctx;
    }

    private static async Task AwaitAllWithTimeout(IReadOnlyList<Task> tasks, int timeoutSeconds, string failMessage)
    {
        var allTasks = Task.WhenAll(tasks);
        if (await Task.WhenAny(allTasks, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds))) != allTasks)
        {
            Assert.Fail($"Timeout ({timeoutSeconds}s): {failMessage}");
        }
        await allTasks;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static InventoryItemEntity MakeItem(int index, int? categoryId = null) => new()
    {
        Name = $"RemoteItem-{index:D5}",
        Category = $"Cat-{(index % 5) + 1}",
        CategoryId = categoryId,
        Status = InventoryItemStatus.Active,
        Quantity = 1 + (index % 10),
        Location = $"Sala-{(index % 20) + 1}",
        AssignedTo = $"Op-{(index % 10) + 1}",
        SerialNumber = $"SN-REM-{index:D8}",
        AssetTag = $"PAT-REM-{index:D6}",
        Manufacturer = $"Fab-{(index % 3) + 1}",
        Model = $"Mod-{(index % 8) + 1}",
        UnitCostCents = 500 + (index % 3000),
        ItemNumber = index,
        AcquiredAt = DateTime.UtcNow.AddDays(-index),
        WarrantyExpiresAt = index % 3 == 0 ? DateTime.UtcNow.AddDays(30) : null,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    private async Task SeedRemoteAsync(int categoryCount, int itemCount, int movementsPerItem, int maintenancePerItem)
    {
        using var ctx = CreateRemoteContext();

        var categories = Enumerable.Range(1, categoryCount).Select(i => new InventoryCategoryEntity
        {
            Name = $"Cat-{i}",
            DisplayOrder = i,
            Description = $"Categoria remota {i}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        }).ToList();
        ctx.InventoryCategories.AddRange(categories);
        await ctx.SaveChangesAsync();

        var items = Enumerable.Range(1, itemCount).Select(i =>
            MakeItem(i, categories[(i - 1) % categories.Count].Id)).ToList();
        ctx.InventoryItems.AddRange(items);
        await ctx.SaveChangesAsync();

        var movements = new List<InventoryMovementEntity>();
        foreach (var item in items)
        {
            for (var m = 0; m < movementsPerItem; m++)
            {
                movements.Add(new InventoryMovementEntity
                {
                    ItemId = item.Id,
                    MovementType = "Transfer",
                    FromLocation = $"Sala-{m}",
                    ToLocation = $"Sala-{m + 1}",
                    PerformedBy = "Admin",
                    MovedAt = DateTime.UtcNow.AddHours(-m),
                });
            }
        }
        if (movements.Count > 0)
        {
            ctx.InventoryMovements.AddRange(movements);
            await ctx.SaveChangesAsync();
        }

        var maintenance = new List<MaintenanceRecordEntity>();
        foreach (var item in items)
        {
            for (var m = 0; m < maintenancePerItem; m++)
            {
                maintenance.Add(new MaintenanceRecordEntity
                {
                    ItemId = item.Id,
                    MaintenanceType = m % 2 == 0 ? "Preventive" : "Corrective",
                    Description = $"Manutenção {m}",
                    Status = MaintenanceStatus.Completed,
                    CompletedAt = DateTime.UtcNow.AddDays(-m),
                    CostCents = 1000 + (m * 100),
                    CreatedAt = DateTime.UtcNow,
                });
            }
        }
        if (maintenance.Count > 0)
        {
            ctx.MaintenanceRecords.AddRange(maintenance);
            await ctx.SaveChangesAsync();
        }
    }

    private async Task<(List<InventoryCategoryEntity> cats, List<InventoryItemEntity> items,
        List<InventoryMovementEntity> movs, List<MaintenanceRecordEntity> maint)> ReadRemoteDataAsync()
    {
        using var ctx = CreateRemoteContext();
        var cats = await ctx.InventoryCategories.AsNoTracking().ToListAsync();
        var items = await ctx.InventoryItems.AsNoTracking().ToListAsync();
        var movs = await ctx.InventoryMovements.AsNoTracking().ToListAsync();
        var maint = await ctx.MaintenanceRecords.AsNoTracking().ToListAsync();
        return (cats, items, movs, maint);
    }

    private static async Task ImportViaReflectionAsync(
        InventoryImportService importService,
        List<InventoryCategoryEntity> categories,
        List<InventoryItemEntity> items,
        List<InventoryMovementEntity> movements,
        List<MaintenanceRecordEntity> maintenance,
        bool replaceAll)
    {
        var method = typeof(InventoryImportService).GetMethod("ImportDataAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);

        var task = (Task<ImportResult>)method!.Invoke(importService,
            new object[] { categories, items, movements, maintenance, replaceAll, System.Threading.CancellationToken.None })!;
        await task;
    }

    /// <summary>
    /// Simula exportação remota: lê dados do source e insere no remote (alvo).
    /// Como ExportToRemoteSqlServerAsync usa EF Core internamente, simulamos o mesmo fluxo.
    /// </summary>
    private async Task ExportToRemoteAsync(MierukaDbContext sourceCtx, bool replaceAll)
    {
        var items = await sourceCtx.InventoryItems.AsNoTracking().ToListAsync();
        var categories = await sourceCtx.InventoryCategories.AsNoTracking().ToListAsync();
        var movements = await sourceCtx.InventoryMovements.AsNoTracking().ToListAsync();
        var maintenance = await sourceCtx.MaintenanceRecords.AsNoTracking().ToListAsync();

        using var remoteCtx = CreateRemoteContext();

        if (replaceAll)
        {
            await remoteCtx.Database.ExecuteSqlRawAsync("DELETE FROM MaintenanceRecords");
            await remoteCtx.Database.ExecuteSqlRawAsync("DELETE FROM InventoryMovements");
            await remoteCtx.Database.ExecuteSqlRawAsync("DELETE FROM InventoryItems");
            await remoteCtx.Database.ExecuteSqlRawAsync("DELETE FROM InventoryCategories");
        }

        var autoDetect = remoteCtx.ChangeTracker.AutoDetectChangesEnabled;
        remoteCtx.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            if (categories.Count > 0)
            {
                remoteCtx.InventoryCategories.AddRange(categories);
                await remoteCtx.SaveChangesAsync();
            }
            if (items.Count > 0)
            {
                // Remap CategoryId para os novos IDs.
                foreach (var item in items)
                {
                    item.Id = 0;
                }
                remoteCtx.InventoryItems.AddRange(items);
                await remoteCtx.SaveChangesAsync();
            }
            if (movements.Count > 0)
            {
                foreach (var mov in movements)
                {
                    mov.Id = 0;
                }
                remoteCtx.InventoryMovements.AddRange(movements);
                await remoteCtx.SaveChangesAsync();
            }
            if (maintenance.Count > 0)
            {
                foreach (var rec in maintenance)
                {
                    rec.Id = 0;
                }
                remoteCtx.MaintenanceRecords.AddRange(maintenance);
                await remoteCtx.SaveChangesAsync();
            }
        }
        finally
        {
            remoteCtx.ChangeTracker.AutoDetectChangesEnabled = autoDetect;
        }
    }

    private static List<InventoryCategoryEntity> CloneCategories(List<InventoryCategoryEntity> source) =>
        source.Select(c => new InventoryCategoryEntity
        {
            Id = c.Id,
            Name = c.Name,
            Icon = c.Icon,
            Description = c.Description,
            DisplayOrder = c.DisplayOrder,
            CustomFieldsJson = c.CustomFieldsJson,
            Color = c.Color,
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt,
        }).ToList();

    private static List<InventoryItemEntity> CloneItems(List<InventoryItemEntity> source) =>
        source.Select(i => new InventoryItemEntity
        {
            Id = i.Id,
            Name = i.Name,
            CategoryId = i.CategoryId,
            Category = i.Category,
            Description = i.Description,
            SerialNumber = i.SerialNumber,
            AssetTag = i.AssetTag,
            Manufacturer = i.Manufacturer,
            Model = i.Model,
            Location = i.Location,
            Status = i.Status,
            Quantity = i.Quantity,
            AssignedTo = i.AssignedTo,
            Notes = i.Notes,
            LinkedMonitorStableId = i.LinkedMonitorStableId,
            ItemNumber = i.ItemNumber,
            MetadataJson = i.MetadataJson,
            CustomFieldValuesJson = i.CustomFieldValuesJson,
            UnitCostCents = i.UnitCostCents,
            AcquiredAt = i.AcquiredAt,
            WarrantyExpiresAt = i.WarrantyExpiresAt,
            CreatedAt = i.CreatedAt,
            UpdatedAt = i.UpdatedAt,
        }).ToList();

    private static List<InventoryMovementEntity> CloneMovements(List<InventoryMovementEntity> source) =>
        source.Select(m => new InventoryMovementEntity
        {
            Id = m.Id,
            ItemId = m.ItemId,
            MovementType = m.MovementType,
            FromLocation = m.FromLocation,
            ToLocation = m.ToLocation,
            FromAssignee = m.FromAssignee,
            ToAssignee = m.ToAssignee,
            PerformedBy = m.PerformedBy,
            Notes = m.Notes,
            MovedAt = m.MovedAt,
        }).ToList();

    private static List<MaintenanceRecordEntity> CloneMaintenance(List<MaintenanceRecordEntity> source) =>
        source.Select(r => new MaintenanceRecordEntity
        {
            Id = r.Id,
            ItemId = r.ItemId,
            MaintenanceType = r.MaintenanceType,
            Description = r.Description,
            PerformedBy = r.PerformedBy,
            CostCents = r.CostCents,
            Status = r.Status,
            ScheduledAt = r.ScheduledAt,
            CompletedAt = r.CompletedAt,
            Notes = r.Notes,
            CreatedAt = r.CreatedAt,
        }).ToList();

    // ═══════════════════════════════════════════════════════════════════════════
    // 1. Import de servidor remoto — modo Replace
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ImportFromRemote_ReplaceMode_AllDataPreserved()
    {
        await SeedRemoteAsync(categoryCount: 5, itemCount: 100, movementsPerItem: 2, maintenancePerItem: 1);
        var (cats, items, movs, maint) = await ReadRemoteDataAsync();

        using var localCtx = CreateLocalContext();
        var importService = new InventoryImportService(localCtx);
        await ImportViaReflectionAsync(importService, cats, items, movs, maint, replaceAll: true);

        using var verifyCtx = CreateLocalContext();
        Assert.Equal(5, await verifyCtx.InventoryCategories.CountAsync());
        Assert.Equal(100, await verifyCtx.InventoryItems.CountAsync());
        Assert.Equal(200, await verifyCtx.InventoryMovements.CountAsync());
        Assert.Equal(100, await verifyCtx.MaintenanceRecords.CountAsync());
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 2. Import de servidor remoto — modo Append
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ImportFromRemote_AppendMode_PreservesExisting()
    {
        // Dados existentes no local.
        using (var existingCtx = CreateLocalContext())
        {
            existingCtx.InventoryCategories.Add(new InventoryCategoryEntity
            {
                Name = "LocalCategory",
                DisplayOrder = 1,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await existingCtx.SaveChangesAsync();
            existingCtx.InventoryItems.Add(MakeItem(99999));
            await existingCtx.SaveChangesAsync();
        }

        await SeedRemoteAsync(categoryCount: 3, itemCount: 50, movementsPerItem: 1, maintenancePerItem: 1);
        var (cats, items, movs, maint) = await ReadRemoteDataAsync();

        using var localCtx = CreateLocalContext();
        var importService = new InventoryImportService(localCtx);
        await ImportViaReflectionAsync(importService, cats, items, movs, maint, replaceAll: false);

        using var verifyCtx = CreateLocalContext();
        Assert.Equal(4, await verifyCtx.InventoryCategories.CountAsync());
        Assert.Equal(51, await verifyCtx.InventoryItems.CountAsync());
        Assert.NotNull(await verifyCtx.InventoryCategories.FirstOrDefaultAsync(c => c.Name == "LocalCategory"));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 3. Export para servidor remoto — modo Replace
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExportToRemote_ReplaceMode_ClearsData()
    {
        // Dados existentes no remoto.
        using (var existingCtx = CreateRemoteContext())
        {
            existingCtx.InventoryItems.Add(MakeItem(77777));
            await existingCtx.SaveChangesAsync();
        }

        // Dados no local para exportar.
        using (var localCtx = CreateLocalContext())
        {
            var cat = new InventoryCategoryEntity
            {
                Name = "ExportCat",
                DisplayOrder = 1,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            localCtx.InventoryCategories.Add(cat);
            await localCtx.SaveChangesAsync();
            for (var i = 0; i < 30; i++)
            {
                localCtx.InventoryItems.Add(MakeItem(i, cat.Id));
            }
            await localCtx.SaveChangesAsync();
        }

        using var sourceCtx = CreateLocalContext();
        await ExportToRemoteAsync(sourceCtx, replaceAll: true);

        using var verifyCtx = CreateRemoteContext();
        var itemCount = await verifyCtx.InventoryItems.CountAsync();
        Assert.Equal(30, itemCount);
        // O item antigo (77777) deve ter sido removido.
        Assert.Null(await verifyCtx.InventoryItems.FirstOrDefaultAsync(i => i.Name == "RemoteItem-77777"));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 4. Export para servidor remoto — modo Append
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExportToRemote_AppendMode_PreservesData()
    {
        // Dados existentes no remoto.
        using (var existingCtx = CreateRemoteContext())
        {
            existingCtx.InventoryItems.Add(MakeItem(66666));
            await existingCtx.SaveChangesAsync();
        }

        // Dados no local para exportar.
        using (var localCtx = CreateLocalContext())
        {
            for (var i = 0; i < 20; i++)
            {
                localCtx.InventoryItems.Add(MakeItem(i));
            }
            await localCtx.SaveChangesAsync();
        }

        using var sourceCtx = CreateLocalContext();
        await ExportToRemoteAsync(sourceCtx, replaceAll: false);

        using var verifyCtx = CreateRemoteContext();
        var itemCount = await verifyCtx.InventoryItems.CountAsync();
        Assert.Equal(21, itemCount); // 1 existente + 20 exportados
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 5. Import remoto — FK remapping
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ImportFromRemote_RemapsIds_FKsCorrect()
    {
        await SeedRemoteAsync(categoryCount: 2, itemCount: 20, movementsPerItem: 2, maintenancePerItem: 1);
        var (cats, items, movs, maint) = await ReadRemoteDataAsync();

        using var localCtx = CreateLocalContext();
        var importService = new InventoryImportService(localCtx);
        await ImportViaReflectionAsync(importService, cats, items, movs, maint, replaceAll: false);

        using var verifyCtx = CreateLocalContext();
        var importedItems = await verifyCtx.InventoryItems.ToListAsync();
        var importedCategories = await verifyCtx.InventoryCategories.ToListAsync();
        var importedMovements = await verifyCtx.InventoryMovements.ToListAsync();
        var importedMaintenance = await verifyCtx.MaintenanceRecords.ToListAsync();

        var categoryIds = importedCategories.Select(c => c.Id).ToHashSet();
        foreach (var item in importedItems)
        {
            if (item.CategoryId.HasValue)
            {
                Assert.Contains(item.CategoryId.Value, categoryIds);
            }
        }

        var itemIds = importedItems.Select(i => i.Id).ToHashSet();
        foreach (var mov in importedMovements)
        {
            Assert.Contains(mov.ItemId, itemIds);
        }
        foreach (var rec in importedMaintenance)
        {
            Assert.Contains(rec.ItemId, itemIds);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 6. Deadlock — imports remotos concorrentes
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ConcurrentRemoteImports_NoDeadlock()
    {
        await SeedRemoteAsync(categoryCount: 2, itemCount: 30, movementsPerItem: 1, maintenancePerItem: 1);
        var (cats, items, movs, maint) = await ReadRemoteDataAsync();

        const int importCount = 5;
        var tasks = new List<Task>();

        for (var t = 0; t < importCount; t++)
        {
            tasks.Add(Task.Run(async () =>
            {
                using var ctx = CreateLocalContext();
                var importSvc = new InventoryImportService(ctx);
                await ImportViaReflectionAsync(importSvc,
                    CloneCategories(cats), CloneItems(items),
                    CloneMovements(movs), CloneMaintenance(maint),
                    replaceAll: false);
            }));
        }

        await AwaitAllWithTimeout(tasks, 120, $"Múltiplos imports remotos paralelos ({importCount}) — possível deadlock.");

        using var verifyCtx = CreateLocalContext();
        var totalItems = await verifyCtx.InventoryItems.CountAsync();
        Assert.Equal(importCount * 30, totalItems);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 7. Deadlock — exports remotos concorrentes
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ConcurrentRemoteExports_NoDeadlock()
    {
        // Popular local com dados para exportar.
        using (var localCtx = CreateLocalContext())
        {
            for (var i = 0; i < 50; i++)
            {
                localCtx.InventoryItems.Add(MakeItem(i));
            }
            await localCtx.SaveChangesAsync();
        }

        const int exportCount = 5;
        var tasks = new List<Task>();

        for (var t = 0; t < exportCount; t++)
        {
            tasks.Add(Task.Run(async () =>
            {
                using var ctx = CreateLocalContext();
                // Simular export: ler tudo do local (como faz ExportToRemoteSqlServerAsync).
                await ctx.InventoryItems.AsNoTracking().ToListAsync();
                await ctx.InventoryCategories.AsNoTracking().ToListAsync();
                await ctx.InventoryMovements.AsNoTracking().ToListAsync();
                await ctx.MaintenanceRecords.AsNoTracking().ToListAsync();
            }));
        }

        await AwaitAllWithTimeout(tasks, 60, $"Múltiplos exports remotos paralelos ({exportCount}) — possível deadlock.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 8. Deadlock — import remoto + CRUD local concorrente
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RemoteImportWithLocalWrites_NoDeadlock()
    {
        await SeedRemoteAsync(categoryCount: 3, itemCount: 50, movementsPerItem: 1, maintenancePerItem: 0);
        var (cats, items, movs, maint) = await ReadRemoteDataAsync();

        var tasks = new List<Task>();

        // Import remoto.
        tasks.Add(Task.Run(async () =>
        {
            using var ctx = CreateLocalContext();
            var importSvc = new InventoryImportService(ctx);
            await ImportViaReflectionAsync(importSvc,
                CloneCategories(cats), CloneItems(items),
                CloneMovements(movs), CloneMaintenance(maint),
                replaceAll: false);
        }));

        // Escritas locais concorrentes.
        for (var t = 0; t < 3; t++)
        {
            var taskIndex = t;
            tasks.Add(Task.Run(async () =>
            {
                using var ctx = CreateLocalContext();
                var service = new InventoryService(ctx);
                for (var i = 0; i < 20; i++)
                {
                    await service.CreateAsync(MakeItem(60000 + taskIndex * 1000 + i));
                }
            }));
        }

        await AwaitAllWithTimeout(tasks, 90, "Import remoto + escritas locais — possível deadlock.");

        using var verifyCtx = CreateLocalContext();
        var totalItems = await verifyCtx.InventoryItems.CountAsync();
        Assert.Equal(50 + 60, totalItems); // 50 importados + 60 criados localmente
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 9. Deadlock — export remoto + leituras locais concorrentes
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RemoteExportWithLocalReads_NoDeadlock()
    {
        // Popular local com dados.
        using (var localCtx = CreateLocalContext())
        {
            for (var i = 0; i < 100; i++)
            {
                localCtx.InventoryItems.Add(MakeItem(i));
            }
            await localCtx.SaveChangesAsync();
        }

        var tasks = new List<Task>();

        // Export remoto (ler dados do local repetidamente).
        tasks.Add(Task.Run(async () =>
        {
            using var ctx = CreateLocalContext();
            for (var i = 0; i < 10; i++)
            {
                await ctx.InventoryItems.AsNoTracking().ToListAsync();
                await ctx.InventoryCategories.AsNoTracking().ToListAsync();
                await ctx.InventoryMovements.AsNoTracking().ToListAsync();
                await ctx.MaintenanceRecords.AsNoTracking().ToListAsync();
            }
        }));

        // Leituras concorrentes.
        for (var t = 0; t < 4; t++)
        {
            tasks.Add(Task.Run(async () =>
            {
                using var ctx = CreateLocalContext();
                var service = new InventoryService(ctx);
                for (var i = 0; i < 20; i++)
                {
                    await service.GetAllAsync();
                    await service.GetStatusSummaryAsync();
                    await service.CountAsync();
                }
            }));
        }

        await AwaitAllWithTimeout(tasks, 60, "Export remoto + leituras locais — possível deadlock.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 10. Deadlock — import + export + CRUD misto
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MixedRemoteImportExport_NoDeadlock()
    {
        await SeedRemoteAsync(categoryCount: 3, itemCount: 50, movementsPerItem: 1, maintenancePerItem: 1);
        var (cats, items, movs, maint) = await ReadRemoteDataAsync();

        var tasks = new List<Task>();

        // Import remoto.
        tasks.Add(Task.Run(async () =>
        {
            using var ctx = CreateLocalContext();
            var importSvc = new InventoryImportService(ctx);
            await ImportViaReflectionAsync(importSvc,
                CloneCategories(cats), CloneItems(items),
                CloneMovements(movs), CloneMaintenance(maint),
                replaceAll: false);
        }));

        // CRUD local.
        for (var t = 0; t < 2; t++)
        {
            var taskIndex = t;
            tasks.Add(Task.Run(async () =>
            {
                using var ctx = CreateLocalContext();
                var service = new InventoryService(ctx);
                for (var i = 0; i < 25; i++)
                {
                    await service.CreateAsync(MakeItem(40000 + taskIndex * 1000 + i));
                }
            }));
        }

        // Export (leituras pesadas do local).
        tasks.Add(Task.Run(async () =>
        {
            using var ctx = CreateLocalContext();
            for (var i = 0; i < 5; i++)
            {
                await ctx.InventoryItems.AsNoTracking().ToListAsync();
                await ctx.InventoryCategories.AsNoTracking().ToListAsync();
            }
        }));

        // Analytics.
        tasks.Add(Task.Run(async () =>
        {
            using var ctx = CreateLocalContext();
            var service = new InventoryService(ctx);
            for (var i = 0; i < 15; i++)
            {
                await service.CountAsync();
                await service.GetTotalValueAsync();
                await service.GetStatusSummaryAsync();
            }
        }));

        await AwaitAllWithTimeout(tasks, 120, "Import + Export + CRUD misto — possível deadlock.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 11. Stress — roundtrip grande (1000 itens)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task LargeRemoteRoundtrip_1000Items()
    {
        await SeedRemoteAsync(categoryCount: 10, itemCount: 1000, movementsPerItem: 2, maintenancePerItem: 1);
        var (cats, items, movs, maint) = await ReadRemoteDataAsync();

        Assert.Equal(10, cats.Count);
        Assert.Equal(1000, items.Count);
        Assert.Equal(2000, movs.Count);
        Assert.Equal(1000, maint.Count);

        using var localCtx = CreateLocalContext();
        var importService = new InventoryImportService(localCtx);
        await ImportViaReflectionAsync(importService, cats, items, movs, maint, replaceAll: true);

        using var verifyCtx = CreateLocalContext();
        Assert.Equal(10, await verifyCtx.InventoryCategories.CountAsync());
        Assert.Equal(1000, await verifyCtx.InventoryItems.CountAsync());
        Assert.Equal(2000, await verifyCtx.InventoryMovements.CountAsync());
        Assert.Equal(1000, await verifyCtx.MaintenanceRecords.CountAsync());
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 12. Import de servidor remoto vazio
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ImportFromEmptyRemote_ReturnsZero()
    {
        // Remoto vazio — nenhum seed.
        var (cats, items, movs, maint) = await ReadRemoteDataAsync();

        Assert.Empty(cats);
        Assert.Empty(items);
        Assert.Empty(movs);
        Assert.Empty(maint);

        using var localCtx = CreateLocalContext();
        var importService = new InventoryImportService(localCtx);
        await ImportViaReflectionAsync(importService, cats, items, movs, maint, replaceAll: false);

        using var verifyCtx = CreateLocalContext();
        Assert.Equal(0, await verifyCtx.InventoryItems.CountAsync());
        Assert.Equal(0, await verifyCtx.InventoryCategories.CountAsync());
    }
}
