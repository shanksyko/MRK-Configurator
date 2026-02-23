#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
/// Testes de estresse para o inventário: carga de 5000 itens, concorrência e deadlocks.
/// Usa SQLite baseado em arquivo temporário com WAL + busy_timeout para concorrência real.
/// </summary>
public sealed class InventoryStressTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _connectionString;

    public InventoryStressTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"mieruka_stress_{Guid.NewGuid():N}.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ConnectionString;

        using var ctx = CreateContext();
        ctx.Database.EnsureCreated();
        // WAL persiste no arquivo — todas as conexões futuras herdam.
        ctx.Database.ExecuteSqlRaw("PRAGMA journal_mode = WAL;");
        ctx.Database.ExecuteSqlRaw("PRAGMA synchronous = NORMAL;");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { /* best-effort */ }
        try { File.Delete(_dbPath + "-wal"); } catch { }
        try { File.Delete(_dbPath + "-shm"); } catch { }
    }

    /// <summary>
    /// Cria um DbContext com conexão aberta e busy_timeout configurado.
    /// busy_timeout é per-connection — precisa ser configurado em cada novo contexto.
    /// </summary>
    private MierukaDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MierukaDbContext>()
            .UseSqlite(_connectionString)
            .Options;
        var ctx = new MierukaDbContext(options);
        ctx.Database.OpenConnection();
        ctx.Database.ExecuteSqlRaw("PRAGMA busy_timeout = 10000;");
        return ctx;
    }

    /// <summary>
    /// Aguarda todas as tasks com timeout. Falha com mensagem clara se timeout ou exceção interna.
    /// </summary>
    private static async Task AwaitAllWithTimeout(IReadOnlyList<Task> tasks, int timeoutSeconds, string failMessage)
    {
        var allTasks = Task.WhenAll(tasks);
        if (await Task.WhenAny(allTasks, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds))) != allTasks)
        {
            Assert.Fail($"Timeout ({timeoutSeconds}s): {failMessage}");
        }
        await allTasks; // Propaga exceções internas das tasks.
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static InventoryItemEntity MakeItem(int index, string category = "Equipamento") => new()
    {
        Name = $"Item-{index:D5}",
        Category = category,
        Status = InventoryItemStatus.Active,
        Quantity = 1 + (index % 10),
        Location = $"Sala-{(index % 50) + 1}",
        AssignedTo = $"Operador-{(index % 20) + 1}",
        SerialNumber = $"SN-{index:D8}",
        AssetTag = $"PAT-{index:D6}",
        Manufacturer = $"Fab-{(index % 5) + 1}",
        Model = $"Modelo-{(index % 15) + 1}",
        UnitCostCents = 1000 + (index % 5000),
        AcquiredAt = DateTime.UtcNow.AddDays(-index),
        WarrantyExpiresAt = index % 3 == 0 ? DateTime.UtcNow.AddDays(30 - (index % 60)) : null,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    private async Task SeedCategoriesAsync(MierukaDbContext ctx, int count = 10)
    {
        var categories = Enumerable.Range(1, count).Select(i => new InventoryCategoryEntity
        {
            Name = $"Cat-{i}",
            DisplayOrder = i,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        }).ToList();

        ctx.InventoryCategories.AddRange(categories);
        await ctx.SaveChangesAsync();
    }

    private async Task<List<InventoryItemEntity>> SeedItemsAsync(MierukaDbContext ctx, int count)
    {
        var items = Enumerable.Range(1, count).Select(i => MakeItem(i)).ToList();
        ctx.InventoryItems.AddRange(items);
        await ctx.SaveChangesAsync();
        return items;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 1. SEED 5000 ITENS E VALIDAÇÃO
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Seed5000Items_AllPersisted()
    {
        using var ctx = CreateContext();
        await SeedItemsAsync(ctx, 5000);

        var totalCount = await ctx.InventoryItems.CountAsync();
        Assert.Equal(5000, totalCount);
    }

    [Fact]
    public async Task Seed5000Items_BulkInsert_CompletesWithinTimeout()
    {
        using var ctx = CreateContext();

        var sw = Stopwatch.StartNew();
        await SeedItemsAsync(ctx, 5000);
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30),
            $"Inserção de 5000 itens demorou {sw.Elapsed.TotalSeconds:F2}s");
    }

    [Fact]
    public async Task Seed5000Items_GetAll_ReturnsCorrectCount()
    {
        using var ctx = CreateContext();
        await SeedItemsAsync(ctx, 5000);

        var service = new InventoryService(ctx);
        var all = await service.GetAllAsync();
        Assert.Equal(5000, all.Count);
    }

    [Fact]
    public async Task Seed5000Items_Search_ReturnsFilteredResults()
    {
        using var ctx = CreateContext();
        await SeedItemsAsync(ctx, 5000);

        var service = new InventoryService(ctx);
        var results = await service.SearchAsync("Item-00001");
        Assert.True(results.Count >= 1, "Busca deveria retornar ao menos 1 resultado para 'Item-00001'.");
    }

    [Fact]
    public async Task Seed5000Items_CountByStatus_IsCorrect()
    {
        using var ctx = CreateContext();
        await SeedItemsAsync(ctx, 5000);

        var service = new InventoryService(ctx);
        var statusSummary = await service.GetStatusSummaryAsync();
        var total = statusSummary.Values.Sum();
        Assert.Equal(5000, total);
    }

    [Fact]
    public async Task Seed5000Items_GetTotalValue_IsPositive()
    {
        using var ctx = CreateContext();
        await SeedItemsAsync(ctx, 5000);

        var service = new InventoryService(ctx);
        var totalValue = await service.GetTotalValueAsync();
        Assert.True(totalValue > 0, "Valor total deveria ser positivo.");
    }

    [Fact]
    public async Task Seed5000Items_CategorySummary_HasEntries()
    {
        using var ctx = CreateContext();
        await SeedItemsAsync(ctx, 5000);

        var service = new InventoryService(ctx);
        var summary = await service.GetCategorySummaryAsync();
        Assert.True(summary.Count > 0, "Deveria ter ao menos uma categoria.");
    }

    [Fact]
    public async Task Seed5000Items_LocationSummary_Has50Locations()
    {
        using var ctx = CreateContext();
        await SeedItemsAsync(ctx, 5000);

        var service = new InventoryService(ctx);
        var summary = await service.GetLocationSummaryAsync();
        Assert.Equal(50, summary.Count);
    }

    [Fact]
    public async Task Seed5000Items_ExpiringWarranty_ReturnsItems()
    {
        using var ctx = CreateContext();
        await SeedItemsAsync(ctx, 5000);

        var service = new InventoryService(ctx);
        var expiring = await service.GetExpiringWarrantyAsync(60);
        Assert.True(expiring.Count > 0, "Deveria ter itens com garantia expirando.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 2. DEADLOCK / CONCORRÊNCIA — Escritas paralelas
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ConcurrentCreates_NoDeadlock()
    {
        const int taskCount = 20;
        const int itemsPerTask = 50;
        var tasks = new List<Task>();

        for (var t = 0; t < taskCount; t++)
        {
            var taskIndex = t;
            tasks.Add(Task.Run(async () =>
            {
                using var ctx = CreateContext();
                var service = new InventoryService(ctx);
                for (var i = 0; i < itemsPerTask; i++)
                {
                    var item = MakeItem(taskIndex * 10000 + i);
                    await service.CreateAsync(item);
                }
            }));
        }

        await AwaitAllWithTimeout(tasks, 90,
            $"Escritas paralelas ({taskCount} tasks x {itemsPerTask} itens) — possível deadlock.");

        using var verifyCtx = CreateContext();
        var count = await verifyCtx.InventoryItems.CountAsync();
        Assert.Equal(taskCount * itemsPerTask, count);
    }

    [Fact]
    public async Task ConcurrentReadsAndWrites_NoDeadlock()
    {
        using (var seedCtx = CreateContext())
        {
            await SeedItemsAsync(seedCtx, 500);
        }

        const int concurrency = 10;
        var tasks = new List<Task>();

        for (var t = 0; t < concurrency; t++)
        {
            if (t % 2 == 0)
            {
                tasks.Add(Task.Run(async () =>
                {
                    using var ctx = CreateContext();
                    var service = new InventoryService(ctx);
                    for (var i = 0; i < 20; i++)
                    {
                        await service.GetAllAsync();
                        await service.GetStatusSummaryAsync();
                        await service.GetCategorySummaryAsync();
                    }
                }));
            }
            else
            {
                var taskIndex = t;
                tasks.Add(Task.Run(async () =>
                {
                    using var ctx = CreateContext();
                    var service = new InventoryService(ctx);
                    for (var i = 0; i < 20; i++)
                    {
                        var item = MakeItem(50000 + taskIndex * 1000 + i);
                        await service.CreateAsync(item);
                    }
                }));
            }
        }

        await AwaitAllWithTimeout(tasks, 60, "Leituras e escritas paralelas — possível deadlock.");
    }

    [Fact]
    public async Task ConcurrentUpdates_SameItems_NoDeadlock()
    {
        List<int> itemIds;
        using (var seedCtx = CreateContext())
        {
            var seeded = await SeedItemsAsync(seedCtx, 100);
            itemIds = seeded.Select(i => i.Id).ToList();
        }

        const int concurrency = 10;
        var tasks = new List<Task>();

        for (var t = 0; t < concurrency; t++)
        {
            var taskIndex = t;
            tasks.Add(Task.Run(async () =>
            {
                using var ctx = CreateContext();
                var service = new InventoryService(ctx);
                for (var i = taskIndex; i < itemIds.Count; i += concurrency)
                {
                    var item = await service.GetByIdAsync(itemIds[i]);
                    if (item is null) continue;
                    item.Location = $"Sala-Atualizada-{taskIndex}";
                    item.Notes = $"Atualizado por task {taskIndex}";
                    await service.UpdateAsync(item);
                }
            }));
        }

        await AwaitAllWithTimeout(tasks, 60, "Atualizações paralelas nos mesmos itens — possível deadlock.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 3. DEADLOCK — Movimentações concorrentes
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ConcurrentMovements_NoDeadlock()
    {
        List<int> itemIds;
        using (var seedCtx = CreateContext())
        {
            var seeded = await SeedItemsAsync(seedCtx, 200);
            itemIds = seeded.Select(i => i.Id).ToList();
        }

        const int concurrency = 10;
        const int movementsPerTask = 20;
        var tasks = new List<Task>();

        for (var t = 0; t < concurrency; t++)
        {
            var taskIndex = t;
            tasks.Add(Task.Run(async () =>
            {
                using var ctx = CreateContext();
                var movService = new InventoryMovementService(ctx);
                for (var i = 0; i < movementsPerTask; i++)
                {
                    var itemId = itemIds[(taskIndex * movementsPerTask + i) % itemIds.Count];
                    await movService.RecordMovementAsync(
                        itemId, "Transfer",
                        $"Sala-{i}", $"Sala-{i + 1}",
                        $"Op-{taskIndex}", $"Op-{taskIndex + 1}",
                        $"Admin-{taskIndex}",
                        $"Movimentação de teste {taskIndex}-{i}");
                }
            }));
        }

        await AwaitAllWithTimeout(tasks, 60, "Movimentações paralelas — possível deadlock.");

        using var verifyCtx = CreateContext();
        var movementCount = await verifyCtx.InventoryMovements.CountAsync();
        Assert.Equal(concurrency * movementsPerTask, movementCount);
    }

    [Fact]
    public async Task ConcurrentMovementsAndReads_NoDeadlock()
    {
        List<int> itemIds;
        using (var seedCtx = CreateContext())
        {
            var seeded = await SeedItemsAsync(seedCtx, 200);
            itemIds = seeded.Select(i => i.Id).ToList();
        }

        const int concurrency = 8;
        var tasks = new List<Task>();

        for (var t = 0; t < concurrency; t++)
        {
            var taskIndex = t;
            if (t % 2 == 0)
            {
                tasks.Add(Task.Run(async () =>
                {
                    using var ctx = CreateContext();
                    var movService = new InventoryMovementService(ctx);
                    for (var i = 0; i < 25; i++)
                    {
                        var itemId = itemIds[(taskIndex * 25 + i) % itemIds.Count];
                        await movService.RecordMovementAsync(
                            itemId, "Transfer",
                            $"De-{taskIndex}", $"Para-{taskIndex}",
                            null, null, $"Admin-{taskIndex}");
                    }
                }));
            }
            else
            {
                tasks.Add(Task.Run(async () =>
                {
                    using var ctx = CreateContext();
                    var movService = new InventoryMovementService(ctx);
                    for (var i = 0; i < 25; i++)
                    {
                        var itemId = itemIds[i % itemIds.Count];
                        await movService.GetMovementHistoryAsync(itemId);
                    }
                }));
            }
        }

        await AwaitAllWithTimeout(tasks, 60, "Movimentações e leituras paralelas — possível deadlock.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 4. DEADLOCK — Manutenções concorrentes
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ConcurrentMaintenanceCreates_NoDeadlock()
    {
        List<int> itemIds;
        using (var seedCtx = CreateContext())
        {
            var seeded = await SeedItemsAsync(seedCtx, 100);
            itemIds = seeded.Select(i => i.Id).ToList();
        }

        const int concurrency = 10;
        const int recordsPerTask = 15;
        var tasks = new List<Task>();

        for (var t = 0; t < concurrency; t++)
        {
            var taskIndex = t;
            tasks.Add(Task.Run(async () =>
            {
                using var ctx = CreateContext();
                var maintService = new MaintenanceRecordService(ctx);
                for (var i = 0; i < recordsPerTask; i++)
                {
                    var record = new MaintenanceRecordEntity
                    {
                        ItemId = itemIds[(taskIndex * recordsPerTask + i) % itemIds.Count],
                        MaintenanceType = i % 2 == 0 ? "Preventive" : "Corrective",
                        Description = $"Manutenção de teste {taskIndex}-{i}",
                        PerformedBy = $"Técnico-{taskIndex}",
                        Status = MaintenanceStatus.Scheduled,
                        ScheduledAt = DateTime.UtcNow.AddDays(i),
                        CostCents = 5000 + (i * 100),
                    };
                    await maintService.CreateAsync(record);
                }
            }));
        }

        await AwaitAllWithTimeout(tasks, 60, "Criação paralela de manutenções — possível deadlock.");

        using var verifyCtx = CreateContext();
        var count = await verifyCtx.MaintenanceRecords.CountAsync();
        Assert.Equal(concurrency * recordsPerTask, count);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 5. DEADLOCK — Operações mistas (CRUD + movimentações + manutenções)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MixedConcurrentOperations_NoDeadlock()
    {
        List<int> itemIds;
        using (var seedCtx = CreateContext())
        {
            await SeedCategoriesAsync(seedCtx, 5);
            var seeded = await SeedItemsAsync(seedCtx, 300);
            itemIds = seeded.Select(i => i.Id).ToList();
        }

        var tasks = new List<Task>();

        // Task 1-3: Criar novos itens.
        for (var t = 0; t < 3; t++)
        {
            var taskIndex = t;
            tasks.Add(Task.Run(async () =>
            {
                using var ctx = CreateContext();
                var service = new InventoryService(ctx);
                for (var i = 0; i < 30; i++)
                {
                    var item = MakeItem(90000 + taskIndex * 1000 + i);
                    await service.CreateAsync(item);
                }
            }));
        }

        // Task 4-5: Atualizar itens existentes.
        for (var t = 0; t < 2; t++)
        {
            var taskIndex = t;
            tasks.Add(Task.Run(async () =>
            {
                using var ctx = CreateContext();
                var service = new InventoryService(ctx);
                for (var i = 0; i < 30; i++)
                {
                    var idx = (taskIndex * 30 + i) % itemIds.Count;
                    var item = await service.GetByIdAsync(itemIds[idx]);
                    if (item is null) continue;
                    item.Notes = $"Updated by mixed task {taskIndex} iteration {i}";
                    await service.UpdateAsync(item);
                }
            }));
        }

        // Task 6-7: Registrar movimentações.
        for (var t = 0; t < 2; t++)
        {
            var taskIndex = t;
            tasks.Add(Task.Run(async () =>
            {
                using var ctx = CreateContext();
                var movService = new InventoryMovementService(ctx);
                for (var i = 0; i < 30; i++)
                {
                    var itemId = itemIds[(taskIndex * 30 + i) % itemIds.Count];
                    await movService.RecordMovementAsync(
                        itemId, "Transfer",
                        $"From-{taskIndex}", $"To-{taskIndex}",
                        null, null, $"MixAdmin-{taskIndex}");
                }
            }));
        }

        // Task 8-9: Criar manutenções.
        for (var t = 0; t < 2; t++)
        {
            var taskIndex = t;
            tasks.Add(Task.Run(async () =>
            {
                using var ctx = CreateContext();
                var maintService = new MaintenanceRecordService(ctx);
                for (var i = 0; i < 20; i++)
                {
                    var record = new MaintenanceRecordEntity
                    {
                        ItemId = itemIds[(taskIndex * 20 + i) % itemIds.Count],
                        MaintenanceType = "Preventive",
                        Description = $"Mixed maint {taskIndex}-{i}",
                        Status = MaintenanceStatus.Scheduled,
                        ScheduledAt = DateTime.UtcNow.AddDays(i),
                    };
                    await maintService.CreateAsync(record);
                }
            }));
        }

        // Task 10: Leituras contínuas de analytics.
        tasks.Add(Task.Run(async () =>
        {
            using var ctx = CreateContext();
            var service = new InventoryService(ctx);
            for (var i = 0; i < 10; i++)
            {
                await service.CountAsync();
                await service.GetTotalValueAsync();
                await service.GetCategorySummaryAsync();
                await service.GetStatusSummaryAsync();
                await service.GetLocationSummaryAsync();
                await service.GetExpiringWarrantyAsync(30);
            }
        }));

        await AwaitAllWithTimeout(tasks, 90,
            "Operações mistas concorrentes (CRUD + movimentações + manutenções + analytics) — possível deadlock.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 6. STRESS — Exclusões concorrentes
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ConcurrentDeletes_NoDeadlock()
    {
        List<int> itemIds;
        using (var seedCtx = CreateContext())
        {
            var seeded = await SeedItemsAsync(seedCtx, 200);
            itemIds = seeded.Select(i => i.Id).ToList();
        }

        const int concurrency = 5;
        var partitionSize = itemIds.Count / concurrency;
        var tasks = new List<Task>();

        for (var t = 0; t < concurrency; t++)
        {
            var partition = itemIds.Skip(t * partitionSize).Take(partitionSize).ToList();
            tasks.Add(Task.Run(async () =>
            {
                using var ctx = CreateContext();
                var service = new InventoryService(ctx);
                foreach (var id in partition)
                {
                    await service.DeleteAsync(id);
                }
            }));
        }

        await AwaitAllWithTimeout(tasks, 60, "Exclusões paralelas — possível deadlock.");

        using var verifyCtx = CreateContext();
        var remaining = await verifyCtx.InventoryItems.CountAsync();
        Assert.Equal(0, remaining);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 7. STRESS — Busca concorrente sobre 5000 itens
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ConcurrentSearch_Over5000Items_NoDeadlock()
    {
        using (var seedCtx = CreateContext())
        {
            await SeedItemsAsync(seedCtx, 5000);
        }

        const int concurrency = 10;
        var searchTerms = Enumerable.Range(0, concurrency).Select(i => $"Item-{(i * 500 + 1):D5}").ToList();
        var tasks = new List<Task>();

        for (var t = 0; t < concurrency; t++)
        {
            var term = searchTerms[t];
            tasks.Add(Task.Run(async () =>
            {
                using var ctx = CreateContext();
                var service = new InventoryService(ctx);
                for (var i = 0; i < 10; i++)
                {
                    var results = await service.SearchAsync(term);
                    Assert.True(results.Count >= 1);
                }
            }));
        }

        await AwaitAllWithTimeout(tasks, 60, "Buscas paralelas sobre 5000 itens — possível deadlock.");
    }
}
