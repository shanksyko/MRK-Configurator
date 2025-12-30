using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Mieruka.Core.Security.Models;

namespace Mieruka.Core.Security.Data;

public class SecurityDbContext : DbContext
{
    private readonly string _databasePath;

    public SecurityDbContext()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folder = Path.Combine(appData, "Mieruka", "Security");
        Directory.CreateDirectory(folder);
        _databasePath = Path.Combine(folder, "security.db");
    }

    public SecurityDbContext(DbContextOptions<SecurityDbContext> options) : base(options)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folder = Path.Combine(appData, "Mieruka", "Security");
        Directory.CreateDirectory(folder);
        _databasePath = Path.Combine(folder, "security.db");
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<DashboardCredential> DashboardCredentials => Set<DashboardCredential>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();
    public DbSet<Session> Sessions => Set<Session>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            // Use SqliteConnectionStringBuilder to configure shared cache for better concurrency
            var connectionStringBuilder = new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            };

            optionsBuilder.UseSqlite(connectionStringBuilder.ConnectionString, options =>
            {
                // Configure command timeout for better handling of busy scenarios
                options.CommandTimeout(30);
                
                // Enable automatic retry for transient failures
                // SQLite doesn't have a built-in retry strategy, but we configure timeouts
                // The busy_timeout PRAGMA will handle retries at the SQLite level
            });
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Enable WAL mode for better concurrency
        // WAL allows readers and writers to proceed concurrently
        Database.ExecuteSqlRaw("PRAGMA journal_mode = WAL;");
        Database.ExecuteSqlRaw("PRAGMA synchronous = NORMAL;");
        Database.ExecuteSqlRaw("PRAGMA busy_timeout = 3000;");

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
            entity.Property(e => e.Action).IsRequired();
        });

        modelBuilder.Entity<Session>(entity =>
        {
            entity.ToTable("Sessions");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.SessionToken).IsUnique();
        });

        // Seed default admin user (password: "admin123" - MUST CHANGE on first login)
        SeedDefaultAdmin(modelBuilder);
    }

    private static void SeedDefaultAdmin(ModelBuilder modelBuilder)
    {
        var salt = GenerateSalt();
        var hash = HashPassword("admin123", salt);

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

    private static string GenerateSalt()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static string HashPassword(string password, string salt)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(
            password,
            Convert.FromBase64String(salt),
            100000,
            HashAlgorithmName.SHA256);
        return Convert.ToBase64String(pbkdf2.GetBytes(32));
    }
}
