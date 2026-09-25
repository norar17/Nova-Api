using System.Collections.Concurrent;
using System.Linq.Expressions;
using ECommerce.Domain.Common;
using ECommerce.Domain.Interfaces;
using ECommerce.Infrastructure.Persistence.Mongo;
using MongoDB.Driver;
using MongoDB.Driver.Linq;

namespace ECommerce.Infrastructure.Persistence;

/// <summary>
/// Mongo-backed replacement for the old EF Core Repository&lt;T&gt;. The public shape (method names
/// and signatures on IRepository&lt;T&gt;) is kept identical to the Postgres version wherever
/// possible so most call sites don't change — but the write semantics are different: every method
/// here (Add/Update/Remove/SoftDelete) talks to Mongo immediately, there is no change tracker and
/// nothing is deferred until UnitOfWork.SaveChangesAsync(). That method is kept only so existing
/// `await _unitOfWork.SaveChangesAsync()` call sites still compile; it's a no-op now (see below).
///
/// IMPORTANT — lost transactional guarantee: the old UnitOfWork gave EF's SaveChangesAsync, which
/// wrapped every pending Add/Update/Remove call across ALL repositories in one Postgres transaction.
/// A standalone MongoDB instance (the default for local dev, e.g. `Host=localhost` in the old
/// connection string) does NOT support multi-document ACID transactions — only a replica set or
/// Atlas cluster does. Until this app is pointed at a replica set and session-based transactions are
/// wired in here, multi-step flows (checkout: creating an Order + OrderItems + a Payment, or
/// anything else that used to be "all or nothing") are no longer atomic: if step 2 fails, step 1's
/// write has already persisted. Flag this to whoever owns CheckoutService/OrderService before this
/// goes anywhere near real payments.
/// </summary>
public class Repository<T> : IRepository<T> where T : BaseEntity
{
    private readonly IMongoCollection<T> _collection;

    public Repository(MongoDbContext context)
    {
        _collection = context.Collection<T>();
    }

    public async Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _collection.Find(x => x.Id == id).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await _collection.Find(FilterDefinition<T>.Empty).ToListAsync(cancellationToken);

    /// <summary>
    /// Returns an IQueryable&lt;T&gt; backed by the Mongo collection, so callers can still do
    /// `.Where(...).OrderBy(...).Skip().Take()` etc. as before (MongoDB.Driver 3.x uses plain
    /// IQueryable&lt;T&gt; directly — there's no separate IMongoQueryable&lt;T&gt; wrapper type as
    /// there was in 2.x). Calling `.Include(...)` on this will no longer compile anywhere in the
    /// Application project — that package reference is gone. That's intentional: Mongo has no
    /// server-side join, so every former Include() call site needed a real look and was converted
    /// by hand instead of silently breaking at runtime. `asNoTracking` is accepted for signature
    /// compatibility but has no effect — Mongo documents returned from a query are always plain,
    /// untracked POCOs.
    /// </summary>
    public IQueryable<T> Query(bool asNoTracking = true) => _collection.AsQueryable();

    public async Task<T> AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        await _collection.InsertOneAsync(entity, cancellationToken: cancellationToken);
        return entity;
    }

    public void Update(T entity)
    {
        entity.UpdatedAtUtc = DateTime.UtcNow;
        // Fire-and-forget-free but still synchronous-looking to match the old EF `Update(entity)`
        // call site shape (EF's Update() was itself synchronous, just marking the entity as
        // Modified for the *next* SaveChangesAsync). Since Mongo writes are immediate and this
        // interface method is synchronous, we block on the replace here. Prefer UpdateAsync where
        // you control the call site.
        _collection.ReplaceOne(x => x.Id == entity.Id, entity);
    }

    public void SoftDelete(T entity)
    {
        entity.IsDeleted = true;
        entity.DeletedAtUtc = DateTime.UtcNow;
        Update(entity);
    }

    public void Remove(T entity) => _collection.DeleteOne(x => x.Id == entity.Id);

    public async Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) =>
        await _collection.Find(predicate).AnyAsync(cancellationToken);

    public async Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) =>
        await _collection.Find(predicate).ToListAsync(cancellationToken);

    public async Task<T?> FindOneAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) =>
        await _collection.Find(predicate).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<T>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids as IReadOnlyCollection<Guid> ?? ids.ToList();
        if (idList.Count == 0) return Array.Empty<T>();
        return await _collection.Find(Builders<T>.Filter.In(x => x.Id, idList)).ToListAsync(cancellationToken);
    }

    /// <summary>Async version of Update, exposed for call sites that want a true await instead of blocking.</summary>
    public async Task UpdateAsync(T entity, CancellationToken cancellationToken = default)
    {
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await _collection.ReplaceOneAsync(x => x.Id == entity.Id, entity, cancellationToken: cancellationToken);
    }
}

public class UnitOfWork : IUnitOfWork
{
    private readonly MongoDbContext _context;
    private readonly ConcurrentDictionary<Type, object> _repositories = new();

    public UnitOfWork(MongoDbContext context)
    {
        _context = context;
    }

    public IRepository<T> Repository<T>() where T : BaseEntity
    {
        return (IRepository<T>)_repositories.GetOrAdd(typeof(T), _ => new Repository<T>(_context));
    }

    /// <summary>
    /// No-op. Every Repository&lt;T&gt; write already committed to Mongo synchronously when it was
    /// called (Add/Update/Remove/SoftDelete), so there is nothing left to flush. Kept only so
    /// existing `await _unitOfWork.SaveChangesAsync()` call sites across the Application layer still
    /// compile without touching every one of them. See the class-level remarks on Repository&lt;T&gt;
    /// for the transactional guarantee this gives up compared to the old EF UnitOfWork.
    /// </summary>
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);

    public void Dispose() { }
}
