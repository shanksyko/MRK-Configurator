namespace Mieruka.Core.Security.Models;

/// <summary>
/// Permissão granular de acesso a um recurso específico (site, aplicação, perfil).
/// </summary>
public sealed class ResourcePermission
{
    public int Id { get; set; }

    /// <summary>FK do usuário que recebe a permissão.</summary>
    public int UserId { get; set; }

    /// <summary>Tipo do recurso: Site, Application, Profile, Monitor, Inventory.</summary>
    public string ResourceType { get; set; } = string.Empty;

    /// <summary>ID do recurso (ExternalId do site/app, StableId do monitor, etc.).</summary>
    public string ResourceId { get; set; } = string.Empty;

    /// <summary>Nível de permissão: View, Operate, Edit, Admin.</summary>
    public string PermissionLevel { get; set; } = "View";

    /// <summary>Quem concedeu a permissão.</summary>
    public int? GrantedBy { get; set; }

    /// <summary>Data de expiração (null = sem expiração).</summary>
    public DateTime? ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
