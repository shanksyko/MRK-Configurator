using Mieruka.Core.Security.Models;

namespace Mieruka.Core.Security.Services;

public interface IUserManagementService
{
    Task<List<User>> GetAllUsersAsync();
    Task<User?> GetByIdAsync(int userId);
    Task<(bool Success, string? Error)> CreateUserAsync(string username, string password, UserRole role, string? fullName, string? email);
    Task<(bool Success, string? Error)> UpdateUserAsync(int userId, UserRole role, string? fullName, string? email, bool isActive);
    Task<(bool Success, string? Error)> ResetPasswordAsync(int userId, string newPassword, int performedByUserId);
}
