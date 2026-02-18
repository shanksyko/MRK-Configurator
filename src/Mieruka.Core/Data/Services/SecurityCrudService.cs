using Microsoft.EntityFrameworkCore;
using Mieruka.Core.Security.Models;

namespace Mieruka.Core.Data.Services;

/// <summary>
/// Serviço CRUD completo para o subsistema de segurança
/// (usuários, credenciais, sessões, auditoria).
/// </summary>
public sealed class SecurityCrudService
{
    private readonly MierukaDbContext _context;

    public SecurityCrudService(MierukaDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    // ────────────────── Usuários ──────────────────

    public async Task<IReadOnlyList<User>> GetAllUsersAsync(CancellationToken ct = default)
        => await _context.Users.AsNoTracking().OrderBy(u => u.Username).ToListAsync(ct);

    public Task<User?> GetUserByIdAsync(int id, CancellationToken ct = default)
        => _context.Users.FindAsync([id], ct).AsTask();

    public async Task<User?> GetUserByUsernameAsync(string username, CancellationToken ct = default)
        => await _context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == username, ct);

    public async Task<User> CreateUserAsync(User user, CancellationToken ct = default)
    {
        user.CreatedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;
        _context.Users.Add(user);
        await _context.SaveChangesAsync(ct);
        return user;
    }

    public async Task UpdateUserAsync(User user, CancellationToken ct = default)
    {
        user.UpdatedAt = DateTime.UtcNow;
        _context.Users.Update(user);
        await _context.SaveChangesAsync(ct);
    }

    public async Task DeleteUserAsync(int id, CancellationToken ct = default)
    {
        var user = await _context.Users.FindAsync([id], ct)
            ?? throw new KeyNotFoundException($"User with id {id} not found.");
        _context.Users.Remove(user);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<bool> UserExistsAsync(string username, CancellationToken ct = default)
        => await _context.Users.AnyAsync(u => u.Username == username, ct);

    // ────────────────── Credenciais de Dashboard ──────────────────

    public async Task<IReadOnlyList<DashboardCredential>> GetAllCredentialsAsync(CancellationToken ct = default)
        => await _context.DashboardCredentials.AsNoTracking().OrderBy(c => c.DisplayName).ToListAsync(ct);

    public Task<DashboardCredential?> GetCredentialByIdAsync(int id, CancellationToken ct = default)
        => _context.DashboardCredentials.FindAsync([id], ct).AsTask();

    public async Task<DashboardCredential?> GetCredentialBySiteIdAsync(string siteId, CancellationToken ct = default)
        => await _context.DashboardCredentials.AsNoTracking()
            .FirstOrDefaultAsync(c => c.SiteId == siteId, ct);

    public async Task<DashboardCredential> CreateCredentialAsync(
        DashboardCredential credential, CancellationToken ct = default)
    {
        credential.CreatedAt = DateTime.UtcNow;
        credential.UpdatedAt = DateTime.UtcNow;
        _context.DashboardCredentials.Add(credential);
        await _context.SaveChangesAsync(ct);
        return credential;
    }

    public async Task UpdateCredentialAsync(DashboardCredential credential, CancellationToken ct = default)
    {
        credential.UpdatedAt = DateTime.UtcNow;
        _context.DashboardCredentials.Update(credential);
        await _context.SaveChangesAsync(ct);
    }

    public async Task DeleteCredentialAsync(int id, CancellationToken ct = default)
    {
        var cred = await _context.DashboardCredentials.FindAsync([id], ct)
            ?? throw new KeyNotFoundException($"Credential with id {id} not found.");
        _context.DashboardCredentials.Remove(cred);
        await _context.SaveChangesAsync(ct);
    }

    // ────────────────── Sessões ──────────────────

    public async Task<IReadOnlyList<Session>> GetActiveSessionsAsync(CancellationToken ct = default)
        => await _context.Sessions.AsNoTracking()
            .Where(s => s.IsActive && s.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

    public async Task<Session> CreateSessionAsync(Session session, CancellationToken ct = default)
    {
        session.CreatedAt = DateTime.UtcNow;
        _context.Sessions.Add(session);
        await _context.SaveChangesAsync(ct);
        return session;
    }

    public async Task InvalidateSessionAsync(string sessionToken, CancellationToken ct = default)
    {
        var session = await _context.Sessions
            .FirstOrDefaultAsync(s => s.SessionToken == sessionToken, ct);
        if (session is not null)
        {
            session.IsActive = false;
            await _context.SaveChangesAsync(ct);
        }
    }

    public async Task CleanupExpiredSessionsAsync(CancellationToken ct = default)
    {
        // ExecuteDeleteAsync executa DELETE direto no banco sem carregar entidades em memória.
        await _context.Sessions
            .Where(s => s.ExpiresAt <= DateTime.UtcNow)
            .ExecuteDeleteAsync(ct);
    }

    // ────────────────── Auditoria ──────────────────

    public async Task<IReadOnlyList<AuditLogEntry>> GetAuditLogsAsync(
        int take = 100, int skip = 0, CancellationToken ct = default)
        => await _context.AuditLog.AsNoTracking()
            .OrderByDescending(a => a.Timestamp)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<AuditLogEntry>> GetAuditLogsByUserAsync(
        string username, int take = 100, int skip = 0, CancellationToken ct = default)
        => await _context.AuditLog.AsNoTracking()
            .Where(a => a.Username == username)
            .OrderByDescending(a => a.Timestamp)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

    public async Task LogAuditAsync(AuditLogEntry entry, CancellationToken ct = default)
    {
        entry.Timestamp = DateTime.UtcNow;
        _context.AuditLog.Add(entry);
        await _context.SaveChangesAsync(ct);
    }
}
