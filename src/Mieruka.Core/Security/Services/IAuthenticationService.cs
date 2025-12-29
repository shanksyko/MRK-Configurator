using Mieruka.Core.Security.Models;

namespace Mieruka.Core.Security.Services;

public interface IAuthenticationService
{
    Task<(bool Success, User? User, string? Error)> AuthenticateAsync(string username, string password);
    Task<bool> ChangePasswordAsync(int userId, string oldPassword, string newPassword);
    Task LogoutAsync(int userId);
    User? CurrentUser { get; }
}
