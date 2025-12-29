using Mieruka.Core.Security.Models;

namespace Mieruka.Core.Security.Services;

public interface IAuditLogService
{
    Task LogAsync(int? userId, string action, string? entityType = null, string? entityId = null, string? details = null);
    Task<List<AuditLogEntry>> GetLogsAsync(DateTime? from = null, DateTime? to = null, int? userId = null, int limit = 1000);
}
