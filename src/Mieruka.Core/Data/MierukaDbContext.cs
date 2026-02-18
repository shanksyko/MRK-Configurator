using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Mieruka.Core.Data.Entities;
using Mieruka.Core.Security.Models;

namespace Mieruka.Core.Data;

/// <summary>
/// Contexto de banco de dados unificado — consolida segurança, configuração de domínio e inventário
/// em um único banco SQLite (<c>%LocalAppData%/Mieruka/mieruka.db</c>).
/// </summary>
public sealed class MierukaDbContext : DbContext
{
    private readonly string _databasePath;

    public MierukaDbContext()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folder = Path.Combine(appData, "Mieruka");
        Directory.CreateDirectory(folder);
        _databasePath = Path.Combine(folder, "mieruka.db");
    }

    public MierukaDbContext(DbContextOptions<MierukaDbContext> options) : base(options)
    {
        // Quando opções são injetadas (testes, DI), não causar side-effects no filesystem.
        _databasePath = string.Empty;
    }

    // ── Segurança (migrado de SecurityDbContext) ──
    public DbSet<User> Users => Set<User>();
    public DbSet<DashboardCredential> DashboardCredentials => Set<DashboardCredential>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();
    public DbSet<Session> Sessions => Set<Session>();

    // ── Domínio / Configuração ──
    public DbSet<ApplicationEntity> Applications => Set<ApplicationEntity>();
    public DbSet<SiteEntity> Sites => Set<SiteEntity>();
    public DbSet<MonitorEntity> Monitors => Set<MonitorEntity>();
    public DbSet<ProfileEntity> Profiles => Set<ProfileEntity>();
    public DbSet<CycleItemEntity> CycleItems => Set<CycleItemEntity>();
    public DbSet<AppSettingEntity> AppSettings => Set<AppSettingEntity>();

    // ── Inventário ──
    public DbSet<InventoryItemEntity> InventoryItems => Set<InventoryItemEntity>();
    public DbSet<InventoryCategoryEntity> InventoryCategories => Set<InventoryCategoryEntity>();
    public DbSet<InventoryMovementEntity> InventoryMovements => Set<InventoryMovementEntity>();
    public DbSet<MaintenanceRecordEntity> MaintenanceRecords => Set<MaintenanceRecordEntity>();

    // ── Controle de Acessos ──
    public DbSet<ResourcePermission> ResourcePermissions => Set<ResourcePermission>();
    public DbSet<AccessLogEntry> AccessLogs => Set<AccessLogEntry>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            var csb = new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            };

            optionsBuilder.UseSqlite(csb.ConnectionString, o => o.CommandTimeout(30));
        }
    }

    /// <summary>
    /// Cria o banco, ativa WAL e ajusta PRAGMAs de desempenho.
    /// Chamar uma vez na inicialização da aplicação.
    /// </summary>
    public void EnsureDatabaseConfigured()
    {
        Database.EnsureCreated();
        Database.ExecuteSqlRaw("PRAGMA journal_mode = WAL;");
        Database.ExecuteSqlRaw("PRAGMA synchronous = NORMAL;");
        Database.ExecuteSqlRaw("PRAGMA busy_timeout = 3000;");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ════════════ Segurança ════════════
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Username).IsUnique();
            entity.Property(e => e.Username).IsRequired().HasMaxLength(50);
            entity.Property(e => e.PasswordHash).IsRequired();
            entity.Property(e => e.Role).IsRequired().HasConversion<string>();
        });

        modelBuilder.Entity<DashboardCredential>(entity =>
        {
            entity.ToTable("DashboardCredentials");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.SiteId).IsUnique();
            entity.Property(e => e.SiteId).IsRequired();
        });

        modelBuilder.Entity<AuditLogEntry>(entity =>
        {
            entity.ToTable("AuditLog");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.Username);
            entity.Property(e => e.Action).IsRequired();
        });

        modelBuilder.Entity<Session>(entity =>
        {
            entity.ToTable("Sessions");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.SessionToken).IsUnique();
            entity.HasIndex(e => new { e.IsActive, e.ExpiresAt });
        });

        // ════════════ Aplicações ════════════
        modelBuilder.Entity<ApplicationEntity>(entity =>
        {
            entity.ToTable("Applications");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ExternalId).IsUnique();
            entity.Property(e => e.ExternalId).IsRequired().HasMaxLength(100);
            entity.Property(e => e.ExecutablePath).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.TargetMonitorStableId).HasMaxLength(200);
        });

        // ════════════ Sites ════════════
        modelBuilder.Entity<SiteEntity>(entity =>
        {
            entity.ToTable("Sites");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ExternalId).IsUnique();
            entity.Property(e => e.ExternalId).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Url).IsRequired().HasMaxLength(2000);
            entity.Property(e => e.Browser).IsRequired().HasMaxLength(20);
        });

        // ════════════ Monitores ════════════
        modelBuilder.Entity<MonitorEntity>(entity =>
        {
            entity.ToTable("Monitors");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.StableId).IsUnique();
            entity.Property(e => e.StableId).IsRequired().HasMaxLength(200);
            entity.Property(e => e.DeviceName).HasMaxLength(200);
            entity.Property(e => e.Name).HasMaxLength(200);
        });

        // ════════════ Perfis ════════════
        modelBuilder.Entity<ProfileEntity>(entity =>
        {
            entity.ToTable("Profiles");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ExternalId).IsUnique();
            entity.Property(e => e.ExternalId).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
        });

        // ════════════ Itens de Ciclo ════════════
        modelBuilder.Entity<CycleItemEntity>(entity =>
        {
            entity.ToTable("CycleItems");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ExternalId).IsUnique();
            entity.Property(e => e.ExternalId).IsRequired().HasMaxLength(100);
            entity.Property(e => e.TargetId).IsRequired().HasMaxLength(100);
            entity.Property(e => e.TargetType).IsRequired().HasMaxLength(50);
        });

        // ════════════ Configurações Gerais (key-value) ════════════
        modelBuilder.Entity<AppSettingEntity>(entity =>
        {
            entity.ToTable("AppSettings");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Key).IsUnique();
            entity.Property(e => e.Key).IsRequired().HasMaxLength(100);
        });

        // ════════════ Inventário ════════════
        modelBuilder.Entity<InventoryItemEntity>(entity =>
        {
            entity.ToTable("InventoryItems");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Category).IsRequired().HasMaxLength(100);
            entity.Property(e => e.SerialNumber).HasMaxLength(200);
            entity.Property(e => e.AssetTag).HasMaxLength(100);
            entity.Property(e => e.Manufacturer).HasMaxLength(200);
            entity.Property(e => e.Model).HasMaxLength(200);
            entity.Property(e => e.Location).HasMaxLength(300);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(50);
            entity.Property(e => e.AssignedTo).HasMaxLength(200);
            entity.Property(e => e.LinkedMonitorStableId).HasMaxLength(200);

            entity.HasIndex(e => e.Category);
            entity.HasIndex(e => e.CategoryId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.SerialNumber);
            entity.HasIndex(e => e.AssetTag);
            entity.HasIndex(e => e.LinkedMonitorStableId);
        });

        modelBuilder.Entity<InventoryCategoryEntity>(entity =>
        {
            entity.ToTable("InventoryCategories");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name).IsUnique();
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Icon).HasMaxLength(50);
            entity.Property(e => e.Description).HasMaxLength(500);
        });

        modelBuilder.Entity<InventoryMovementEntity>(entity =>
        {
            entity.ToTable("InventoryMovements");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ItemId);
            entity.HasIndex(e => e.MovedAt);
            entity.Property(e => e.MovementType).IsRequired().HasMaxLength(50);
            entity.Property(e => e.FromLocation).HasMaxLength(300);
            entity.Property(e => e.ToLocation).HasMaxLength(300);
            entity.Property(e => e.FromAssignee).HasMaxLength(200);
            entity.Property(e => e.ToAssignee).HasMaxLength(200);
            entity.Property(e => e.PerformedBy).HasMaxLength(200);
        });

        modelBuilder.Entity<MaintenanceRecordEntity>(entity =>
        {
            entity.ToTable("MaintenanceRecords");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ItemId);
            entity.HasIndex(e => e.ScheduledAt);
            entity.Property(e => e.MaintenanceType).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Description).IsRequired().HasMaxLength(1000);
            entity.Property(e => e.PerformedBy).HasMaxLength(200);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(50);
        });

        // ════════════ Controle de Acessos ════════════
        modelBuilder.Entity<ResourcePermission>(entity =>
        {
            entity.ToTable("ResourcePermissions");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.ResourceType, e.ResourceId }).IsUnique();
            entity.HasIndex(e => e.UserId);
            entity.Property(e => e.ResourceType).IsRequired().HasMaxLength(50);
            entity.Property(e => e.ResourceId).IsRequired().HasMaxLength(200);
            entity.Property(e => e.PermissionLevel).IsRequired().HasMaxLength(20);
        });

        modelBuilder.Entity<AccessLogEntry>(entity =>
        {
            entity.ToTable("AccessLogs");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.AccessedAt);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => new { e.ResourceType, e.ResourceId });
            entity.HasIndex(e => e.Username);
            entity.Property(e => e.Username).IsRequired().HasMaxLength(100);
            entity.Property(e => e.ResourceType).IsRequired().HasMaxLength(50);
            entity.Property(e => e.ResourceId).IsRequired().HasMaxLength(200);
            entity.Property(e => e.ResourceName).HasMaxLength(300);
            entity.Property(e => e.Action).IsRequired().HasMaxLength(50);
            entity.Property(e => e.SourceAddress).HasMaxLength(100);
            entity.Property(e => e.Result).IsRequired().HasMaxLength(20);
        });

        SeedDefaultAdmin(modelBuilder);
    }

    private static void SeedDefaultAdmin(ModelBuilder modelBuilder)
    {
        // Salt e hash pré-computados e determinísticos para o seed.
        // Senha padrão: "admin123" — MustChangePassword = true obriga troca no primeiro login.
        // Se o seed usar RandomNumberGenerator, o EF Core detecta "alteração" a cada instanciação
        // do DbContext e gera migrations fantasma.
        const string salt = "K7gNU3sdo+OL0wNhqoVWhr3g6s1xYv72ol/pe/Unols=";
        const string hash = "MJg7GBbPCPMbwYXrOkYj6fJJcYFNg2FvjpvBMZqxDvY=";

        modelBuilder.Entity<User>().HasData(new User
        {
            Id = 1,
            Username = "admin",
            PasswordHash = hash,
            PasswordSalt = salt,
            Role = UserRole.Admin,
            FullName = "Administrator",
            Email = "admin@local",
            IsActive = true,
            CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            MustChangePassword = true
        });
    }
}
