using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Mieruka.Core.Security.Data;
using Mieruka.Core.Security.Models;

namespace Mieruka.Core.Security.Services;

public sealed class UserManagementService : IUserManagementService
{
    private readonly SecurityDbContext _context;
    private readonly IAuditLogService _auditLog;

    public UserManagementService(SecurityDbContext context, IAuditLogService auditLog)
    {
        _context = context;
        _auditLog = auditLog;
    }

    public async Task<List<User>> GetAllUsersAsync()
    {
        return await _context.Users.OrderBy(u => u.Username).ToListAsync();
    }

    public async Task<User?> GetByIdAsync(int userId)
    {
        return await _context.Users.FindAsync(userId);
    }

    public async Task<(bool Success, string? Error)> CreateUserAsync(
        string username, string password, UserRole role, string? fullName, string? email)
    {
        if (string.IsNullOrWhiteSpace(username))
            return (false, "Nome de usuário é obrigatório.");

        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
            return (false, "Senha deve ter pelo menos 6 caracteres.");

        var exists = await _context.Users.AnyAsync(u => u.Username == username);
        if (exists)
            return (false, $"Usuário '{username}' já existe.");

        var salt = GenerateSalt();
        var user = new User
        {
            Username = username,
            PasswordSalt = salt,
            PasswordHash = HashPassword(password, salt),
            Role = role,
            FullName = fullName,
            Email = email,
            IsActive = true,
            MustChangePassword = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _context.Users.Add(user);
        await SaveChangesWithRetryAsync();
        await _auditLog.LogAsync(null, "User Created", "User", null, $"Username: {username}, Role: {role}");

        return (true, null);
    }

    public async Task<(bool Success, string? Error)> UpdateUserAsync(
        int userId, UserRole role, string? fullName, string? email, bool isActive)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user is null)
            return (false, "Usuário não encontrado.");

        user.Role = role;
        user.FullName = fullName;
        user.Email = email;
        user.IsActive = isActive;
        user.UpdatedAt = DateTime.UtcNow;

        await SaveChangesWithRetryAsync();
        await _auditLog.LogAsync(userId, "User Updated", "User", userId.ToString(),
            $"Role: {role}, Active: {isActive}");

        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ResetPasswordAsync(
        int userId, string newPassword, int performedByUserId)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
            return (false, "Senha deve ter pelo menos 6 caracteres.");

        var user = await _context.Users.FindAsync(userId);
        if (user is null)
            return (false, "Usuário não encontrado.");

        user.PasswordSalt = GenerateSalt();
        user.PasswordHash = HashPassword(newPassword, user.PasswordSalt);
        user.MustChangePassword = true;
        user.UpdatedAt = DateTime.UtcNow;

        await SaveChangesWithRetryAsync();
        await _auditLog.LogAsync(performedByUserId, "Password Reset", "User", userId.ToString(),
            $"Reset for user: {user.Username}");

        return (true, null);
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

    private async Task SaveChangesWithRetryAsync()
    {
        const int maxRetries = 3;
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                await _context.SaveChangesAsync();
                return;
            }
            catch (DbUpdateException) when (i < maxRetries - 1)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * Math.Pow(2, i)));
            }
        }
    }
}
