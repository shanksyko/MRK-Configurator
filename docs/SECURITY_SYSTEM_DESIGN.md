# Security System Design - User Management, Credentials & Audit Log

**Data**: 2024-12-29  
**Escopo**: Sistema completo de segurança com SQLite

---

## 📋 Visão Geral

Implementação de sistema de segurança completo incluindo:
- **A) User Management** - Gerenciamento de usuários com roles
- **B) Credentials Management** - UI para gerenciar credenciais de dashboards
- **C) Audit Log** - Registro completo de ações

**Tecnologia**: SQLite + Entity Framework Core + Windows Forms

---

## 🗄️ Database Schema (SQLite)

### Tabela: Users
```sql
CREATE TABLE Users (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Username TEXT NOT NULL UNIQUE,
    PasswordHash TEXT NOT NULL,
    PasswordSalt TEXT NOT NULL,
    Role TEXT NOT NULL CHECK(Role IN ('Admin', 'Operator', 'Viewer')),
    FullName TEXT,
    Email TEXT,
    IsActive INTEGER NOT NULL DEFAULT 1,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    LastLoginAt TEXT,
    MustChangePassword INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX idx_users_username ON Users(Username);
CREATE INDEX idx_users_active ON Users(IsActive);
```

### Tabela: DashboardCredentials
```sql
CREATE TABLE DashboardCredentials (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SiteId TEXT NOT NULL UNIQUE,
    DisplayName TEXT NOT NULL,
    Username TEXT NOT NULL,
    EncryptedPassword BLOB NOT NULL,
    PasswordIV BLOB NOT NULL,
    Url TEXT,
    Notes TEXT,
    CreatedBy INTEGER NOT NULL,
    CreatedAt TEXT NOT NULL,
    UpdatedBy INTEGER NOT NULL,
    UpdatedAt TEXT NOT NULL,
    FOREIGN KEY (CreatedBy) REFERENCES Users(Id),
    FOREIGN KEY (UpdatedBy) REFERENCES Users(Id)
);

CREATE INDEX idx_credentials_siteid ON DashboardCredentials(SiteId);
```

### Tabela: AuditLog
```sql
CREATE TABLE AuditLog (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId INTEGER,
    Username TEXT NOT NULL,
    Action TEXT NOT NULL,
    EntityType TEXT,
    EntityId TEXT,
    Details TEXT,
    IpAddress TEXT,
    Timestamp TEXT NOT NULL,
    FOREIGN KEY (UserId) REFERENCES Users(Id)
);

CREATE INDEX idx_auditlog_timestamp ON AuditLog(Timestamp DESC);
CREATE INDEX idx_auditlog_user ON AuditLog(UserId);
CREATE INDEX idx_auditlog_action ON AuditLog(Action);
```

### Tabela: Sessions
```sql
CREATE TABLE Sessions (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId INTEGER NOT NULL,
    SessionToken TEXT NOT NULL UNIQUE,
    CreatedAt TEXT NOT NULL,
    ExpiresAt TEXT NOT NULL,
    IsActive INTEGER NOT NULL DEFAULT 1,
    FOREIGN KEY (UserId) REFERENCES Users(Id)
);

CREATE INDEX idx_sessions_token ON Sessions(SessionToken);
CREATE INDEX idx_sessions_expiry ON Sessions(ExpiresAt);
```

---

## 🏗️ Arquitetura

### Estrutura de Pastas
```
src/
├── Mieruka.Core/
│   └── Security/
│       ├── Models/
│       │   ├── User.cs
│       │   ├── DashboardCredential.cs
│       │   ├── AuditLogEntry.cs
│       │   └── UserRole.cs
│       ├── Services/
│       │   ├── IAuthenticationService.cs
│       │   ├── IAuthorizationService.cs
│       │   ├── IAuditLogService.cs
│       │   └── ICredentialsService.cs
│       └── Data/
│           ├── SecurityDbContext.cs
│           └── Migrations/
│
├── Mieruka.App/
│   └── Forms/
│       ├── Security/
│       │   ├── LoginForm.cs
│       │   ├── UserManagementForm.cs
│       │   ├── CredentialsManagementForm.cs
│       │   ├── AuditLogViewerForm.cs
│       │   └── ChangePasswordForm.cs
│       └── MainForm.cs (modificado para requer autenticação)
│
└── Mieruka.Tests/
    └── Security/
        ├── AuthenticationServiceTests.cs
        ├── AuditLogServiceTests.cs
        └── CredentialsServiceTests.cs
```

---

## 🔐 Implementação de Serviços

### 1. SecurityDbContext.cs
```csharp
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

    public DbSet<User> Users => Set<User>();
    public DbSet<DashboardCredential> DashboardCredentials => Set<DashboardCredential>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();
    public DbSet<Session> Sessions => Set<Session>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={_databasePath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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

        // Seed admin user (default password: "admin123" - MUST CHANGE)
        SeedDefaultAdmin(modelBuilder);
    }

    private void SeedDefaultAdmin(ModelBuilder modelBuilder)
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
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            MustChangePassword = true
        });
    }

    private static string GenerateSalt()
    {
        var bytes = new byte[32];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static string HashPassword(string password, string salt)
    {
        using var pbkdf2 = new System.Security.Cryptography.Rfc2898DeriveBytes(
            password, 
            Convert.FromBase64String(salt), 
            100000, 
            System.Security.Cryptography.HashAlgorithmName.SHA256);
        return Convert.ToBase64String(pbkdf2.GetBytes(32));
    }
}
```

### 2. Models/User.cs
```csharp
namespace Mieruka.Core.Security.Models;

public enum UserRole
{
    Admin,      // Full access
    Operator,   // Can manage dashboards, credentials
    Viewer      // Read-only access
}

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public bool MustChangePassword { get; set; }
}
```

### 3. Models/DashboardCredential.cs
```csharp
namespace Mieruka.Core.Security.Models;

public class DashboardCredential
{
    public int Id { get; set; }
    public string SiteId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public byte[] EncryptedPassword { get; set; } = Array.Empty<byte>();
    public byte[] PasswordIV { get; set; } = Array.Empty<byte>();
    public string? Url { get; set; }
    public string? Notes { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public int UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

### 4. Models/AuditLogEntry.cs
```csharp
namespace Mieruka.Core.Security.Models;

public class AuditLogEntry
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? Details { get; set; }
    public string? IpAddress { get; set; }
    public DateTime Timestamp { get; set; }
}
```

### 5. AuthenticationService.cs
```csharp
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Mieruka.Core.Security.Data;
using Mieruka.Core.Security.Models;

namespace Mieruka.Core.Security.Services;

public interface IAuthenticationService
{
    Task<(bool Success, User? User, string? Error)> AuthenticateAsync(string username, string password);
    Task<bool> ChangePasswordAsync(int userId, string oldPassword, string newPassword);
    Task LogoutAsync(int userId);
    User? CurrentUser { get; }
}

public class AuthenticationService : IAuthenticationService
{
    private readonly SecurityDbContext _context;
    private readonly IAuditLogService _auditLog;
    
    public User? CurrentUser { get; private set; }

    public AuthenticationService(SecurityDbContext context, IAuditLogService auditLog)
    {
        _context = context;
        _auditLog = auditLog;
    }

    public async Task<(bool Success, User? User, string? Error)> AuthenticateAsync(string username, string password)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Username == username);

        if (user == null)
        {
            await _auditLog.LogAsync(null, "Login Failed", details: $"User not found: {username}");
            return (false, null, "Invalid username or password");
        }

        if (!user.IsActive)
        {
            await _auditLog.LogAsync(null, "Login Failed", details: $"Inactive user: {username}");
            return (false, null, "Account is disabled");
        }

        var hash = HashPassword(password, user.PasswordSalt);
        if (hash != user.PasswordHash)
        {
            await _auditLog.LogAsync(null, "Login Failed", details: $"Wrong password: {username}");
            return (false, null, "Invalid username or password");
        }

        user.LastLoginAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        
        CurrentUser = user;
        await _auditLog.LogAsync(user.Id, "Login Success", details: $"User {username} logged in");

        return (true, user, user.MustChangePassword ? "You must change your password" : null);
    }

    public async Task<bool> ChangePasswordAsync(int userId, string oldPassword, string newPassword)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null) return false;

        var hash = HashPassword(oldPassword, user.PasswordSalt);
        if (hash != user.PasswordHash) return false;

        user.PasswordSalt = GenerateSalt();
        user.PasswordHash = HashPassword(newPassword, user.PasswordSalt);
        user.MustChangePassword = false;
        user.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        await _auditLog.LogAsync(userId, "Password Changed");

        return true;
    }

    public async Task LogoutAsync(int userId)
    {
        await _auditLog.LogAsync(userId, "Logout");
        CurrentUser = null;
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
```

### 6. AuditLogService.cs
```csharp
using Mieruka.Core.Security.Data;
using Mieruka.Core.Security.Models;

namespace Mieruka.Core.Security.Services;

public interface IAuditLogService
{
    Task LogAsync(int? userId, string action, string? entityType = null, string? entityId = null, string? details = null);
    Task<List<AuditLogEntry>> GetLogsAsync(DateTime? from = null, DateTime? to = null, int? userId = null, int limit = 1000);
}

public class AuditLogService : IAuditLogService
{
    private readonly SecurityDbContext _context;

    public AuditLogService(SecurityDbContext context)
    {
        _context = context;
    }

    public async Task LogAsync(int? userId, string action, string? entityType = null, string? entityId = null, string? details = null)
    {
        var username = userId.HasValue 
            ? (await _context.Users.FindAsync(userId.Value))?.Username ?? "Unknown"
            : "System";

        var entry = new AuditLogEntry
        {
            UserId = userId,
            Username = username,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details,
            Timestamp = DateTime.UtcNow
        };

        _context.AuditLog.Add(entry);
        await _context.SaveChangesAsync();
    }

    public async Task<List<AuditLogEntry>> GetLogsAsync(DateTime? from = null, DateTime? to = null, int? userId = null, int limit = 1000)
    {
        var query = _context.AuditLog.AsQueryable();

        if (from.HasValue)
            query = query.Where(l => l.Timestamp >= from.Value);

        if (to.HasValue)
            query = query.Where(l => l.Timestamp <= to.Value);

        if (userId.HasValue)
            query = query.Where(l => l.UserId == userId.Value);

        return await query
            .OrderByDescending(l => l.Timestamp)
            .Take(limit)
            .ToListAsync();
    }
}
```

---

## 🖥️ UI Forms

### 1. LoginForm.cs
```csharp
using System.Windows.Forms;
using Mieruka.Core.Security.Services;

namespace Mieruka.App.Forms.Security;

public partial class LoginForm : Form
{
    private readonly IAuthenticationService _authService;
    private TextBox _txtUsername;
    private TextBox _txtPassword;
    private Button _btnLogin;
    private Label _lblError;

    public User? AuthenticatedUser { get; private set; }

    public LoginForm(IAuthenticationService authService)
    {
        _authService = authService;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text = "Mieruka - Login";
        Size = new Size(400, 250);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var lblUsername = new Label 
        { 
            Text = "Username:", 
            Location = new Point(30, 30),
            AutoSize = true
        };

        _txtUsername = new TextBox
        {
            Location = new Point(30, 55),
            Width = 330,
            Font = new Font("Segoe UI", 10F)
        };

        var lblPassword = new Label
        {
            Text = "Password:",
            Location = new Point(30, 90),
            AutoSize = true
        };

        _txtPassword = new TextBox
        {
            Location = new Point(30, 115),
            Width = 330,
            UseSystemPasswordChar = true,
            Font = new Font("Segoe UI", 10F)
        };

        _lblError = new Label
        {
            Location = new Point(30, 145),
            Width = 330,
            ForeColor = Color.Red,
            Text = "",
            AutoSize = false
        };

        _btnLogin = new Button
        {
            Text = "Login",
            Location = new Point(30, 175),
            Width = 330,
            Height = 35,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold)
        };
        _btnLogin.Click += BtnLogin_Click;

        Controls.AddRange(new Control[] 
        { 
            lblUsername, _txtUsername, 
            lblPassword, _txtPassword,
            _lblError, _btnLogin 
        });

        AcceptButton = _btnLogin;
        _txtUsername.Focus();
    }

    private async void BtnLogin_Click(object? sender, EventArgs e)
    {
        _lblError.Text = "";
        _btnLogin.Enabled = false;

        try
        {
            var (success, user, error) = await _authService.AuthenticateAsync(
                _txtUsername.Text, 
                _txtPassword.Text);

            if (success && user != null)
            {
                AuthenticatedUser = user;

                if (user.MustChangePassword)
                {
                    MessageBox.Show(
                        "You must change your password before continuing.",
                        "Password Change Required",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);

                    using var changePasswordForm = new ChangePasswordForm(_authService, user.Id);
                    if (changePasswordForm.ShowDialog() == DialogResult.OK)
                    {
                        DialogResult = DialogResult.OK;
                        Close();
                    }
                    else
                    {
                        await _authService.LogoutAsync(user.Id);
                        AuthenticatedUser = null;
                        _btnLogin.Enabled = true;
                    }
                }
                else
                {
                    DialogResult = DialogResult.OK;
                    Close();
                }
            }
            else
            {
                _lblError.Text = error ?? "Login failed";
                _btnLogin.Enabled = true;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error: {ex.Message}", "Login Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _btnLogin.Enabled = true;
        }
    }
}
```

---

## 📦 NuGet Packages Necessários

```xml
<!-- Adicionar em Directory.Packages.props -->
<ItemGroup>
  <PackageVersion Include="Microsoft.EntityFrameworkCore.Sqlite" Version="8.0.0" />
  <PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.0" />
</ItemGroup>
```

```xml
<!-- Adicionar em src/Mieruka.Core/Mieruka.Core.csproj -->
<ItemGroup>
  <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" />
  <PackageReference Include="Microsoft.EntityFrameworkCore.Design" />
</ItemGroup>
```

---

## 🚀 Integração com MainForm

```csharp
// src/Mieruka.App/Program.cs
[STAThread]
static void Main()
{
    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);

    // Initialize security database
    using var dbContext = new SecurityDbContext();
    dbContext.Database.Migrate();

    // Initialize services
    var auditLog = new AuditLogService(dbContext);
    var authService = new AuthenticationService(dbContext, auditLog);

    // Show login form
    using var loginForm = new LoginForm(authService);
    if (loginForm.ShowDialog() != DialogResult.OK)
    {
        return; // User cancelled login
    }

    // Check authorization
    var user = loginForm.AuthenticatedUser;
    if (user == null || user.Role == UserRole.Viewer)
    {
        MessageBox.Show("Insufficient permissions", "Access Denied");
        return;
    }

    // Start main application
    Application.Run(new MainForm(authService, auditLog));
}
```

---

## 📊 Estimativa de Implementação

### Fase 1: Core (3-4 dias)
- ✅ Database schema e migrations
- ✅ Models e DbContext
- ✅ AuthenticationService
- ✅ AuditLogService
- ✅ CredentialsService

### Fase 2: UI Forms (3-4 dias)
- ✅ LoginForm
- ✅ UserManagementForm (CRUD)
- ✅ CredentialsManagementForm (CRUD)
- ✅ AuditLogViewerForm
- ✅ ChangePasswordForm

### Fase 3: Integration (2-3 dias)
- ✅ Integrar com MainForm
- ✅ Proteção de menus por role
- ✅ Session management
- ✅ Auto-lock após inatividade

### Fase 4: Testing (2-3 dias)
- ✅ Unit tests
- ✅ Integration tests
- ✅ Security testing
- ✅ User acceptance testing

**Total**: 10-14 dias (2-3 semanas)

---

## 🔒 Considerações de Segurança

1. **Passwords**: PBKDF2 com 100,000 iterações + SHA256
2. **Credentials**: AES-256 encryption + DPAPI para master key
3. **SQL Injection**: Entity Framework protege automaticamente
4. **Session Management**: Tokens com expiração
5. **Audit Log**: Todas ações registradas (imutável)

---

**Criado por**: GitHub Copilot Agent  
**Data**: 2024-12-29  
**Versão**: 1.0  
**Status**: Design completo - Pronto para implementação
