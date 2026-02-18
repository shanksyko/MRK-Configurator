namespace Mieruka.Core.Security.Models;

public sealed class DashboardCredential
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
