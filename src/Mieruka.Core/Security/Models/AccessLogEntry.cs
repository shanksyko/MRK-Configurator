namespace Mieruka.Core.Security.Models;

/// <summary>
/// Registra o acesso (login/interação) de um usuário a um recurso específico.
/// Usado para auditoria e relatório de acessos por dashboards/sites.
/// </summary>
public sealed class AccessLogEntry
{
    public int Id { get; set; }

    /// <summary>FK do usuário (null para acessos de sistema).</summary>
    public int? UserId { get; set; }

    /// <summary>Nome do usuário (desnormalizado para consulta).</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Tipo do recurso acessado: Site, Application, Profile.</summary>
    public string ResourceType { get; set; } = string.Empty;

    /// <summary>ID do recurso acessado.</summary>
    public string ResourceId { get; set; } = string.Empty;

    /// <summary>Nome exibível do recurso (para relatórios).</summary>
    public string? ResourceName { get; set; }

    /// <summary>Ação realizada: Login, Logout, View, Execute, Configure.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Início do acesso.</summary>
    public DateTime AccessedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Fim do acesso (para calcular duração da sessão).</summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>IP ou hostname de origem.</summary>
    public string? SourceAddress { get; set; }

    /// <summary>Resultado do acesso: Success, Failed, Denied, Timeout.</summary>
    public string Result { get; set; } = "Success";

    /// <summary>Detalhes adicionais (ex: motivo de falha).</summary>
    public string? Details { get; set; }
}
