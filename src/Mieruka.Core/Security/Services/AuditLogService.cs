using Microsoft.EntityFrameworkCore;
using Mieruka.Core.Security.Data;
using Mieruka.Core.Security.Models;

namespace Mieruka.Core.Security.Services;

public sealed class AuditLogService : IAuditLogService
{
    private readonly SecurityDbContext _context;

    public AuditLogService(SecurityDbContext context)
    {
        _context = context;
    }

    public async Task LogAsync(int? userId, string action, string? entityType = null, string? entityId = null, string? details = null)
    {
        // Run database write in background to avoid blocking UI thread
        await Task.Run(async () =>
        {
            var username = userId.HasValue
                ? (await _context.Users.FindAsync(userId.Value).ConfigureAwait(false))?.Username ?? "Unknown"
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
            
            // Retry logic for database writes
            // SQLite busy_timeout will handle most retries, but we add an outer retry
            const int maxRetries = 3;
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    await _context.SaveChangesAsync().ConfigureAwait(false);
                    break;
                }
                catch (DbUpdateException) when (i < maxRetries - 1)
                {
                    // Wait before retry with exponential backoff
                    await Task.Delay(TimeSpan.FromMilliseconds(100 * Math.Pow(2, i))).ConfigureAwait(false);
                }
            }
        }).ConfigureAwait(false);
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
            .ToListAsync().ConfigureAwait(false);
    }
}
