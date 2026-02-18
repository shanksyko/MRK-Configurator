using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Mieruka.Core.Data.Repositories;

/// <summary>
/// Implementação genérica de repositório CRUD usando <see cref="MierukaDbContext"/>.
/// </summary>
public sealed class Repository<T> : IRepository<T> where T : class
{
    private readonly MierukaDbContext Context;
    private readonly DbSet<T> DbSet;

    public Repository(MierukaDbContext context)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        DbSet = context.Set<T>();
    }

    public async Task<T?> GetByIdAsync(int id, CancellationToken ct = default)
        => await DbSet.FindAsync([id], ct);

    public async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default)
        => await DbSet.AsNoTracking().ToListAsync(ct);

    public async Task<IReadOnlyList<T>> FindAsync(
        Expression<Func<T, bool>> predicate, CancellationToken ct = default)
        => await DbSet.AsNoTracking().Where(predicate).ToListAsync(ct);

    public async Task<T> AddAsync(T entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await DbSet.AddAsync(entity, ct);
        await Context.SaveChangesAsync(ct);
        return entity;
    }

    public async Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entities);
        await DbSet.AddRangeAsync(entities, ct);
        await Context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(T entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        DbSet.Update(entity);
        await Context.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await DbSet.FindAsync([id], ct)
            ?? throw new KeyNotFoundException($"Entity of type {typeof(T).Name} with id {id} not found.");
        DbSet.Remove(entity);
        await Context.SaveChangesAsync(ct);
    }

    public async Task<int> CountAsync(CancellationToken ct = default)
        => await DbSet.CountAsync(ct);

    public async Task<bool> ExistsAsync(
        Expression<Func<T, bool>> predicate, CancellationToken ct = default)
        => await DbSet.AnyAsync(predicate, ct);
}
