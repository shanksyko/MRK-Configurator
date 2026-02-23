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
/// Testes de deadlock e integridade para import/export do inventário.
/// Usa dois bancos SQLite em arquivo (source e target) para simular o fluxo completo
/// sem depender de drivers Access ou SQL Server LocalDB.
/// </summary>
public sealed class ImportExportDeadlockTests : IDisposable
{
    private readonly string _sourceDbPath;
    private readonly string _targetDbPath;
    private readonly string _sourceConnectionString;
    private readonly string _targetConnectionString;

    public ImportExportDeadlockTests()
    {
        _sourceDbPath = Path.Combine(Path.GetTempPath(), $"mieruka_export_{Guid.NewGuid():N}.db");
        _targetDbPath = Path.Combine(Path.GetTempPath(), $"mieruka_import_{Guid.NewGuid():N}.db");

        _sourceConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _sourceDbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ConnectionString;

        _targetConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _targetDbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ConnectionString;

        // Inicializar ambos os bancos com WAL.
        using var sourceCtx = CreateSourceContext();
        sourceCtx.Database.EnsureCreated();
        sourceCtx.Database.ExecuteSqlRaw("PRAGMA journal_mode = WAL;");
        sourceCtx.Database.ExecuteSqlRaw("PRAGMA synchronous = NORMAL;");

        using var targetCtx = CreateTargetContext();
        targetCtx.Database.EnsureCreated();
        targetCtx.Database.ExecuteSqlRaw("PRAGMA journal_mode = WAL;");
        targetCtx.Database.ExecuteSqlRaw("PRAGMA synchronous = NORMAL;");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _sourceDbPath, _targetDbPath })
        {
            try { File.Delete(path); } catch { }
            try { File.Delete(path + "-wal"); } catch { }
            try { File.Delete(path + "-shm"); } catch { }
        }
    }

    private MierukaDbContext CreateSourceContext()
    {
        var options = new DbContextOptionsBuilder<MierukaDbContext>()
            .UseSqlite(_sourceConnectionString)
            .Options;
        var ctx = new MierukaDbContext(options);
        ctx.Database.OpenConnection();
        ctx.Database.ExecuteSqlRaw("PRAGMA busy_timeout = 10000;");
        return ctx;
    }

    private MierukaDbContext CreateTargetContext()
    {
        var options = new DbContextOptionsBuilder<MierukaDbContext>()
            .UseSqlite(_targetConnectionString)
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
        Name = $"ExpItem-{index:D5}",
        Category = $"Cat-{(index % 5) + 1}",
        CategoryId = categoryId,
        Status = InventoryItemStatus.Active,
        Quantity = 1 + (index % 10),
        Location = $"Sala-{(index % 20) + 1}",
        AssignedTo = $"Op-{(index % 10) + 1}",
        SerialNumber = $"SN-EXP-{index:D8}",
        AssetTag = $"PAT-EXP-{index:D6}",
        Manufacturer = $"Fab-{(index % 3) + 1}",
        Model = $"Mod-{(index % 8) + 1}",
        UnitCostCents = 500 + (index % 3000),
        ItemNumber = index,
        AcquiredAt = DateTime.UtcNow.AddDays(-index),
        WarrantyExpiresAt = index % 3 == 0 ? DateTime.UtcNow.AddDays(30) : null,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    /// <summary>
    /// Popula o banco de origem com categorias, itens, movimentações e manutenções.
    /// </summary>
    private async Task SeedSourceAsync(int categoryCount, int itemCount, int movementsPerItem, int maintenancePerItem)
    {
        using var ctx = CreateSourceContext();

        // Categorias
        var categories = Enumerable.Range(1, categoryCount).Select(i => new InventoryCategoryEntity
        {
            Name = $"Cat-{i}",
            DisplayOrder = i,
            Description = $"Categoria teste {i}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        }).ToList();
        ctx.InventoryCategories.AddRange(categories);
        await ctx.SaveChangesAsync();

        // Itens
        var items = Enumerable.Range(1, itemCount).Select(i =>
            MakeItem(i, categories[(i - 1) % categories.Count].Id)).ToList();
        ctx.InventoryItems.AddRange(items);
        await ctx.SaveChangesAsync();

        // Movimentações
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

        // Manutenções
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

    /// <summary>
    /// Lê todos os dados do banco de origem (simulando o que Access/SQL Server readers fazem).
    /// </summary>
    private async Task<(List<InventoryCategoryEntity> cats, List<InventoryItemEntity> items,
        List<InventoryMovementEntity> movs, List<MaintenanceRecordEntity> maint)> ReadSourceDataAsync()
    {
        using var ctx = CreateSourceContext();
        var cats = await ctx.InventoryCategories.AsNoTracking().ToListAsync();
        var items = await ctx.InventoryItems.AsNoTracking().ToListAsync();
        var movs = await ctx.InventoryMovements.AsNoTracking().ToListAsync();
        var maint = await ctx.MaintenanceRecords.AsNoTracking().ToListAsync();
        return (cats, items, movs, maint);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 1. ROUNDTRIP — Exportar do source, importar no target (Replace)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReplaceImport_Roundtrip_AllDataPreserved()
    {
        await SeedSourceAsync(categoryCount: 5, itemCount: 100, movementsPerItem: 2, maintenancePerItem: 1);
        var (cats, items, movs, maint) = await ReadSourceDataAsync();

        using var targetCtx = CreateTargetContext();
        var importService = new InventoryImportService(targetCtx);

        // Simula o ImportDataAsync interno passando replaceAll: true.
        // Chamamos o serviço com dados já lidos (como se fosse Access ou SQL Server).
        await ImportViaReflectionAsync(importService, cats, items, movs, maint, replaceAll: true);

        // Verificar dados no target.
        using var verifyCtx = CreateTargetContext();
        Assert.Equal(5, await verifyCtx.InventoryCategories.CountAsync());
        Assert.Equal(100, await verifyCtx.InventoryItems.CountAsync());
        Assert.Equal(200, await verifyCtx.InventoryMovements.CountAsync());
        Assert.Equal(100, await verifyCtx.MaintenanceRecords.CountAsync());
    }

    [Fact]
    public async Task ReplaceImport_ClearsExistingData()
    {
        // Seed existing data in target.
        using (var existingCtx = CreateTargetContext())
        {
            existingCtx.InventoryCategories.Add(new InventoryCategoryEntity
            {
                Name = "OldCategory",
                DisplayOrder = 1,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await existingCtx.SaveChangesAsync();
            existingCtx.InventoryItems.Add(MakeItem(99999));
            await existingCtx.SaveChangesAsync();
        }

        // Seed source with new data.
        await SeedSourceAsync(categoryCount: 3, itemCount: 50, movementsPerItem: 1, maintenancePerItem: 0);
        var (cats, items, movs, maint) = await ReadSourceDataAsync();

        using var targetCtx = CreateTargetContext();
        var importService = new InventoryImportService(targetCtx);
        await ImportViaReflectionAsync(importService, cats, items, movs, maint, replaceAll: true);

        using var verifyCtx = CreateTargetContext();
        Assert.Equal(3, await verifyCtx.InventoryCategories.CountAsync());
        Assert.Equal(50, await verifyCtx.InventoryItems.CountAsync());
        // Old data must be gone.
        Assert.Null(await verifyCtx.InventoryCategories.FirstOrDefaultAsync(c => c.Name == "OldCategory"));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 2. APPEND — Importar sem apagar dados existentes
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AppendImport_PreservesExistingData()
    {
        // Seed existing data in target.
        using (var existingCtx = CreateTargetContext())
        {
            existingCtx.InventoryCategories.Add(new InventoryCategoryEntity
            {
                Name = "ExistingCategory",
                DisplayOrder = 1,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await existingCtx.SaveChangesAsync();
            var existingItem = MakeItem(88888);
            existingItem.Category = "ExistingCategory";
            existingCtx.InventoryItems.Add(existingItem);
            await existingCtx.SaveChangesAsync();
        }

        await SeedSourceAsync(categoryCount: 3, itemCount: 50, movementsPerItem: 1, maintenancePerItem: 1);
        var (cats, items, movs, maint) = await ReadSourceDataAsync();

        using var targetCtx = CreateTargetContext();
        var importService = new InventoryImportService(targetCtx);
        await ImportViaReflectionAsync(importService, cats, items, movs, maint, replaceAll: false);

        using var verifyCtx = CreateTargetContext();
        // 1 existing + 3 imported = 4 categories.
        Assert.Equal(4, await verifyCtx.InventoryCategories.CountAsync());
        // 1 existing + 50 imported = 51 items.
        Assert.Equal(51, await verifyCtx.InventoryItems.CountAsync());
        Assert.Equal(50, await verifyCtx.InventoryMovements.CountAsync());
        Assert.Equal(50, await verifyCtx.MaintenanceRecords.CountAsync());
        // Existing data still present.
        Assert.NotNull(await verifyCtx.InventoryCategories.FirstOrDefaultAsync(c => c.Name == "ExistingCategory"));
    }

    [Fact]
    public async Task AppendImport_RemapsIds_ForeignKeysCorrect()
    {
        await SeedSourceAsync(categoryCount: 2, itemCount: 20, movementsPerItem: 2, maintenancePerItem: 1);
        var (cats, items, movs, maint) = await ReadSourceDataAsync();

        using var targetCtx = CreateTargetContext();
        var importService = new InventoryImportService(targetCtx);
        await ImportViaReflectionAsync(importService, cats, items, movs, maint, replaceAll: false);

        using var verifyCtx = CreateTargetContext();
        var importedItems = await verifyCtx.InventoryItems.ToListAsync();
        var importedCategories = await verifyCtx.InventoryCategories.ToListAsync();
        var importedMovements = await verifyCtx.InventoryMovements.ToListAsync();
        var importedMaintenance = await verifyCtx.MaintenanceRecords.ToListAsync();

        // Todos os CategoryIds dos itens devem referir a categorias que existem.
        var categoryIds = importedCategories.Select(c => c.Id).ToHashSet();
        foreach (var item in importedItems)
        {
            if (item.CategoryId.HasValue)
            {
                Assert.Contains(item.CategoryId.Value, categoryIds);
            }
        }

        // Todos os ItemIds das movimentações devem referir a itens existentes.
        var itemIds = importedItems.Select(i => i.Id).ToHashSet();
        foreach (var mov in importedMovements)
        {
            Assert.Contains(mov.ItemId, itemIds);
        }

        // Todos os ItemIds das manutenções devem referir a itens existentes.
        foreach (var rec in importedMaintenance)
        {
            Assert.Contains(rec.ItemId, itemIds);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 3. DEADLOCK — Importação concorrente com leituras
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ConcurrentImportAndReads_NoDeadlock()
    {
        await SeedSourceAsync(categoryCount: 3, itemCount: 100, movementsPerItem: 1, maintenancePerItem: 1);
        var (cats, items, movs, maint) = await ReadSourceDataAsync();

        // Pré-popular target para que leituras tenham dados.
        using (var preCtx = CreateTargetContext())
        {
            preCtx.InventoryCategories.Add(new InventoryCategoryEntity
            {
                Name = "Pre",
                DisplayOrder = 1,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await preCtx.SaveChangesAsync();
            for (var i = 0; i < 50; i++)
            {
                preCtx.InventoryItems.Add(MakeItem(70000 + i));
            }
            await preCtx.SaveChangesAsync();
        }

        var tasks = new List<Task>();

        // Task 1: Importar (append).
        tasks.Add(Task.Run(async () =>
        {
            using var ctx = CreateTargetContext();
            var importSvc = new InventoryImportService(ctx);
            await ImportViaReflectionAsync(importSvc, CloneData(cats), CloneData(items), CloneData(movs), CloneData(maint), replaceAll: false);
        }));

        // Tasks 2-5: Leituras concorrentes no target.
        for (var t = 0; t < 4; t++)
        {
            tasks.Add(Task.Run(async () =>
            {
                using var ctx = CreateTargetContext();
                var service = new InventoryService(ctx);
                for (var i = 0; i < 20; i++)
                {
                    await service.GetAllAsync();
                    await service.GetStatusSummaryAsync();
                    await service.CountAsync();
                }
            }));
        }

        await AwaitAllWithTimeout(tasks, 90, "Importação concorrente com leituras — possível deadlock.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 4. DEADLOCK — Importação concorrente com escritas
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ConcurrentImportAndWrites_NoDeadlock()
    {
        await SeedSourceAsync(categoryCount: 2, itemCount: 50, movementsPerItem: 1, maintenancePerItem: 0);
        var (cats, items, movs, maint) = await ReadSourceDataAsync();

        var tasks = new List<Task>();

        // Task 1: Importar (append).
        tasks.Add(Task.Run(async () =>
        {
            using var ctx = CreateTargetContext();
            var importSvc = new InventoryImportService(ctx);
            await ImportViaReflectionAsync(importSvc, CloneData(cats), CloneData(items), CloneData(movs), CloneData(maint), replaceAll: false);
        }));

        // Tasks 2-4: Escritas concorrentes no target.
        for (var t = 0; t < 3; t++)
        {
            var taskIndex = t;
            tasks.Add(Task.Run(async () =>
            {
                using var ctx = CreateTargetContext();
                var service = new InventoryService(ctx);
                for (var i = 0; i < 30; i++)
                {
                    var item = MakeItem(60000 + taskIndex * 1000 + i);
                    await service.CreateAsync(item);
                }
            }));
        }

        await AwaitAllWithTimeout(tasks, 90, "Importação concorrente com escritas — possível deadlock.");

        using var verifyCtx = CreateTargetContext();
        var totalItems = await verifyCtx.InventoryItems.CountAsync();
        // 50 imported + 90 written = 140.
        Assert.Equal(140, totalItems);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 5. DEADLOCK — Múltiplas importações concorrentes (append)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MultipleParallelImports_Append_NoDeadlock()
    {
        await SeedSourceAsync(categoryCount: 2, itemCount: 30, movementsPerItem: 1, maintenancePerItem: 1);
        var (cats, items, movs, maint) = await ReadSourceDataAsync();

        const int importCount = 5;
        var tasks = new List<Task>();

        for (var t = 0; t < importCount; t++)
        {
            tasks.Add(Task.Run(async () =>
            {
                using var ctx = CreateTargetContext();
                var importSvc = new InventoryImportService(ctx);
                await ImportViaReflectionAsync(importSvc, CloneData(cats), CloneData(items), CloneData(movs), CloneData(maint), replaceAll: false);
            }));
        }

        await AwaitAllWithTimeout(tasks, 120, $"Múltiplas importações paralelas ({importCount}) — possível deadlock.");

        using var verifyCtx = CreateTargetContext();
        // Cada importação adiciona 30 itens com novos IDs.
        var totalItems = await verifyCtx.InventoryItems.CountAsync();
        Assert.Equal(importCount * 30, totalItems);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 6. STRESS — Roundtrip com 1000 itens e relações
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task LargeRoundtrip_1000Items_NoDeadlock()
    {
        await SeedSourceAsync(categoryCount: 10, itemCount: 1000, movementsPerItem: 2, maintenancePerItem: 1);
        var (cats, items, movs, maint) = await ReadSourceDataAsync();

        Assert.Equal(10, cats.Count);
        Assert.Equal(1000, items.Count);
        Assert.Equal(2000, movs.Count);
        Assert.Equal(1000, maint.Count);

        using var targetCtx = CreateTargetContext();
        var importService = new InventoryImportService(targetCtx);
        await ImportViaReflectionAsync(importService, cats, items, movs, maint, replaceAll: true);

        using var verifyCtx = CreateTargetContext();
        Assert.Equal(10, await verifyCtx.InventoryCategories.CountAsync());
        Assert.Equal(1000, await verifyCtx.InventoryItems.CountAsync());
        Assert.Equal(2000, await verifyCtx.InventoryMovements.CountAsync());
        Assert.Equal(1000, await verifyCtx.MaintenanceRecords.CountAsync());
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 7. DEADLOCK — Exportação concorrente com importação
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ConcurrentExportAndImport_NoDeadlock()
    {
        // Seed source and target com dados.
        await SeedSourceAsync(categoryCount: 3, itemCount: 80, movementsPerItem: 1, maintenancePerItem: 1);
        var (cats, items, movs, maint) = await ReadSourceDataAsync();

        // Target tem dados existentes para exportar.
        using (var preCtx = CreateTargetContext())
        {
            for (var i = 0; i < 40; i++)
            {
                preCtx.InventoryItems.Add(MakeItem(80000 + i));
            }
            await preCtx.SaveChangesAsync();
        }

        var tasks = new List<Task>();

        // Task 1: Importar ao target (append mode).
        tasks.Add(Task.Run(async () =>
        {
            using var ctx = CreateTargetContext();
            var importSvc = new InventoryImportService(ctx);
            await ImportViaReflectionAsync(importSvc, CloneData(cats), CloneData(items), CloneData(movs), CloneData(maint), replaceAll: false);
        }));

        // Task 2: Exportar do target enquanto importação roda (simula leitura pesada).
        tasks.Add(Task.Run(async () =>
        {
            using var ctx = CreateTargetContext();
            // Simular exportação: ler tudo repetidamente.
            for (var i = 0; i < 5; i++)
            {
                await ctx.InventoryCategories.AsNoTracking().ToListAsync();
                await ctx.InventoryItems.AsNoTracking().ToListAsync();
                await ctx.InventoryMovements.AsNoTracking().ToListAsync();
                await ctx.MaintenanceRecords.AsNoTracking().ToListAsync();
            }
        }));

        // Tasks 3-4: Mais leituras concorrentes.
        for (var i = 0; i < 2; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                using var ctx = CreateTargetContext();
                var service = new InventoryService(ctx);
                for (var j = 0; j < 10; j++)
                {
                    await service.GetAllAsync();
                    await service.GetCategorySummaryAsync();
                }
            }));
        }

        await AwaitAllWithTimeout(tasks, 90, "Export + Import concorrentes — possível deadlock.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 8. STRESS — Import/Export misto com operações CRUD concorrentes
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MixedImportExportCrud_NoDeadlock()
    {
        await SeedSourceAsync(categoryCount: 3, itemCount: 50, movementsPerItem: 1, maintenancePerItem: 1);
        var (cats, items, movs, maint) = await ReadSourceDataAsync();

        var tasks = new List<Task>();

        // Task 1: Importar (append).
        tasks.Add(Task.Run(async () =>
        {
            using var ctx = CreateTargetContext();
            var importSvc = new InventoryImportService(ctx);
            await ImportViaReflectionAsync(importSvc, CloneData(cats), CloneData(items), CloneData(movs), CloneData(maint), replaceAll: false);
        }));

        // Task 2-3: Criar itens no target.
        for (var t = 0; t < 2; t++)
        {
            var taskIndex = t;
            tasks.Add(Task.Run(async () =>
            {
                using var ctx = CreateTargetContext();
                var service = new InventoryService(ctx);
                for (var i = 0; i < 25; i++)
                {
                    await service.CreateAsync(MakeItem(40000 + taskIndex * 1000 + i));
                }
            }));
        }

        // Task 4: Leituras de analytics.
        tasks.Add(Task.Run(async () =>
        {
            using var ctx = CreateTargetContext();
            var service = new InventoryService(ctx);
            for (var i = 0; i < 15; i++)
            {
                await service.CountAsync();
                await service.GetTotalValueAsync();
                await service.GetStatusSummaryAsync();
            }
        }));

        // Task 5: Exportar (ler tudo).
        tasks.Add(Task.Run(async () =>
        {
            using var ctx = CreateTargetContext();
            for (var i = 0; i < 5; i++)
            {
                await ctx.InventoryItems.AsNoTracking().ToListAsync();
                await ctx.InventoryCategories.AsNoTracking().ToListAsync();
            }
        }));

        await AwaitAllWithTimeout(tasks, 120,
            "Operações mistas (import + export + CRUD + analytics) — possível deadlock.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // UTILITÁRIOS
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Invoca ImportDataAsync internamente via construção e chamada direta do serviço de importação.
    /// Como ImportDataAsync é privado, chamamos diretamente o fluxo público simulado.
    /// </summary>
    private static async Task ImportViaReflectionAsync(
        InventoryImportService importService,
        List<InventoryCategoryEntity> categories,
        List<InventoryItemEntity> items,
        List<InventoryMovementEntity> movements,
        List<MaintenanceRecordEntity> maintenance,
        bool replaceAll)
    {
        // Usar reflection para acessar ImportDataAsync (private).
        var method = typeof(InventoryImportService).GetMethod("ImportDataAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);

        var task = (Task<ImportResult>)method!.Invoke(importService,
            new object[] { categories, items, movements, maintenance, replaceAll, System.Threading.CancellationToken.None })!;
        await task;
    }

    /// <summary>
    /// Clona entidades para que cada import paralelo tenha sua própria cópia
    /// (evita mutação concorrente das listas originais, dado que InsertWithNewIdsAsync modifica os objetos).
    /// </summary>
    private static List<InventoryCategoryEntity> CloneData(List<InventoryCategoryEntity> source) =>
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

    private static List<InventoryItemEntity> CloneData(List<InventoryItemEntity> source) =>
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

    private static List<InventoryMovementEntity> CloneData(List<InventoryMovementEntity> source) =>
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

    private static List<MaintenanceRecordEntity> CloneData(List<MaintenanceRecordEntity> source) =>
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
}
