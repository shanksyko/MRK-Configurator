using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Mieruka.Core.Security.Data;
using Mieruka.Core.Security.Models;

namespace Mieruka.Core.Security.Services;

public sealed class AuthenticationService : IAuthenticationService
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
        await SaveChangesWithRetryAsync();

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

        await SaveChangesWithRetryAsync();
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

    private async Task SaveChangesWithRetryAsync()
    {
        // Retry logic for database writes with exponential backoff
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
                // Wait before retry with exponential backoff
                await Task.Delay(TimeSpan.FromMilliseconds(100 * Math.Pow(2, i)));
            }
        }
    }
}
