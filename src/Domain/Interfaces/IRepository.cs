using System.Linq.Expressions;
using ECommerce.Domain.Common;

namespace ECommerce.Domain.Interfaces;

/// <summary>
/// Generic repository abstraction. Concrete implementation lives in Infrastructure.
/// Application layer depends only on this interface (Dependency Inversion).
/// </summary>
public interface IRepository<T> where T : BaseEntity
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default);
    IQueryable<T> Query(bool asNoTracking = true);
    Task<T> AddAsync(T entity, CancellationToken cancellationToken = default);
    void Update(T entity);
    /// <summary>True async version of <see cref="Update"/>. Prefer this in new/converted code.</summary>
    Task UpdateAsync(T entity, CancellationToken cancellationToken = default);
    void SoftDelete(T entity);
    /// <summary>
    /// Hard-deletes the row. Reserved for non-auditable, ephemeral data (e.g. cart items) —
    /// anything with financial or historical value should use <see cref="SoftDelete"/> instead.
    /// </summary>
    void Remove(T entity);
    Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);

    // --- Added for the Mongo conversion ---
    // Mongo has no server-side joins/Include(), so code that used to write
    // `repo.Query().Include(x => x.Related)` now fetches the related documents itself. These two
    // helpers cover the common cases: one predicate lookup, and one batch "WHERE Id IN (...)" lookup
    // (the standard way to resolve a set of foreign keys in a document database).
    Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);
    Task<T?> FindOneAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<T>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
}

public interface IUnitOfWork : IDisposable
{
    IRepository<T> Repository<T>() where T : BaseEntity;
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
