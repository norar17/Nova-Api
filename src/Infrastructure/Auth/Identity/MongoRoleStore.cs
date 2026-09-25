using ECommerce.Domain.Entities;
using ECommerce.Infrastructure.Persistence.Mongo;
using Microsoft.AspNetCore.Identity;
using MongoDB.Driver;

namespace ECommerce.Infrastructure.Auth.Identity;

/// <summary>Hand-written replacement for the EF role store, used by RoleManager&lt;ApplicationRole&gt;.</summary>
public class MongoRoleStore : IRoleStore<ApplicationRole>, IQueryableRoleStore<ApplicationRole>
{
    private readonly IMongoCollection<ApplicationRole> _roles;

    public MongoRoleStore(MongoDbContext context)
    {
        _roles = context.Roles;
    }

    public IQueryable<ApplicationRole> Roles => _roles.AsQueryable();

    public async Task<IdentityResult> CreateAsync(ApplicationRole role, CancellationToken ct)
    {
        await _roles.InsertOneAsync(role, cancellationToken: ct);
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> UpdateAsync(ApplicationRole role, CancellationToken ct)
    {
        await _roles.ReplaceOneAsync(r => r.Id == role.Id, role, cancellationToken: ct);
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> DeleteAsync(ApplicationRole role, CancellationToken ct)
    {
        await _roles.DeleteOneAsync(r => r.Id == role.Id, ct);
        return IdentityResult.Success;
    }

    public Task<string> GetRoleIdAsync(ApplicationRole role, CancellationToken ct) => Task.FromResult(role.Id.ToString());
    public Task<string?> GetRoleNameAsync(ApplicationRole role, CancellationToken ct) => Task.FromResult(role.Name);
    public Task SetRoleNameAsync(ApplicationRole role, string? roleName, CancellationToken ct) { role.Name = roleName; return Task.CompletedTask; }
    public Task<string?> GetNormalizedRoleNameAsync(ApplicationRole role, CancellationToken ct) => Task.FromResult(role.NormalizedName);
    public Task SetNormalizedRoleNameAsync(ApplicationRole role, string? normalizedName, CancellationToken ct) { role.NormalizedName = normalizedName; return Task.CompletedTask; }

    public Task<ApplicationRole?> FindByIdAsync(string roleId, CancellationToken ct) =>
        Guid.TryParse(roleId, out var id)
            ? _roles.Find(r => r.Id == id).FirstOrDefaultAsync(ct)!
            : Task.FromResult<ApplicationRole?>(null);

    public Task<ApplicationRole?> FindByNameAsync(string normalizedRoleName, CancellationToken ct) =>
        _roles.Find(r => r.NormalizedName == normalizedRoleName).FirstOrDefaultAsync(ct)!;

    public void Dispose() { }
}
