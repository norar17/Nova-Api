using ECommerce.Domain.Entities;
using ECommerce.Infrastructure.Persistence.Mongo;
using Microsoft.AspNetCore.Identity;
using MongoDB.Driver;

namespace ECommerce.Infrastructure.Auth.Identity;

/// <summary>
/// Hand-written replacement for Microsoft.AspNetCore.Identity.EntityFrameworkCore's user store —
/// there is no official Microsoft-maintained Mongo equivalent. Implements exactly the Identity
/// store interfaces this app's UserManager usage actually exercises (see AuthService, ReviewFeature,
/// ChatFeature, StoreFeature, DbSeeder): password auth, email lookup/confirmation, role membership,
/// lockout, security stamp, and the `Users` queryable UserManager.Users relies on. Two-factor,
/// external logins, phone numbers, and claims are NOT implemented since nothing in the app uses
/// them — add the matching interface + members here first if that changes.
/// </summary>
public class MongoUserStore :
    IUserStore<ApplicationUser>,
    IUserEmailStore<ApplicationUser>,
    IUserPasswordStore<ApplicationUser>,
    IUserRoleStore<ApplicationUser>,
    IUserLockoutStore<ApplicationUser>,
    IUserSecurityStampStore<ApplicationUser>,
    IQueryableUserStore<ApplicationUser>
{
    private readonly IMongoCollection<ApplicationUser> _users;
    private readonly IMongoCollection<ApplicationRole> _roles;
    private readonly IMongoCollection<MongoUserRole> _userRoles;

    public MongoUserStore(MongoDbContext context)
    {
        _users = context.Users;
        _roles = context.Roles;
        _userRoles = context.Database.GetCollection<MongoUserRole>("UserRoles");
    }

    public IQueryable<ApplicationUser> Users => _users.AsQueryable();

    // --- IUserStore ---

    public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.Id.ToString());
    public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.UserName);
    public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken ct) { user.UserName = userName; return Task.CompletedTask; }
    public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.NormalizedUserName);
    public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken ct) { user.NormalizedUserName = normalizedName; return Task.CompletedTask; }

    public async Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken ct)
    {
        await _users.InsertOneAsync(user, cancellationToken: ct);
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken ct)
    {
        await _users.ReplaceOneAsync(u => u.Id == user.Id, user, cancellationToken: ct);
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken ct)
    {
        await _users.DeleteOneAsync(u => u.Id == user.Id, ct);
        await _userRoles.DeleteManyAsync(ur => ur.UserId == user.Id, ct);
        return IdentityResult.Success;
    }

    public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken ct) =>
        Guid.TryParse(userId, out var id)
            ? _users.Find(u => u.Id == id).FirstOrDefaultAsync(ct)!
            : Task.FromResult<ApplicationUser?>(null);

    public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken ct) =>
        _users.Find(u => u.NormalizedUserName == normalizedUserName).FirstOrDefaultAsync(ct)!;

    // --- IUserEmailStore ---

    public Task SetEmailAsync(ApplicationUser user, string? email, CancellationToken ct) { user.Email = email; return Task.CompletedTask; }
    public Task<string?> GetEmailAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.Email);
    public Task<bool> GetEmailConfirmedAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.EmailConfirmed);
    public Task SetEmailConfirmedAsync(ApplicationUser user, bool confirmed, CancellationToken ct) { user.EmailConfirmed = confirmed; return Task.CompletedTask; }
    public Task<ApplicationUser?> FindByEmailAsync(string normalizedEmail, CancellationToken ct) =>
        _users.Find(u => u.NormalizedEmail == normalizedEmail).FirstOrDefaultAsync(ct)!;
    public Task<string?> GetNormalizedEmailAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.NormalizedEmail);
    public Task SetNormalizedEmailAsync(ApplicationUser user, string? normalizedEmail, CancellationToken ct) { user.NormalizedEmail = normalizedEmail; return Task.CompletedTask; }

    // --- IUserPasswordStore ---

    public Task SetPasswordHashAsync(ApplicationUser user, string? hash, CancellationToken ct) { user.PasswordHash = hash; return Task.CompletedTask; }
    public Task<string?> GetPasswordHashAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.PasswordHash);
    public Task<bool> HasPasswordAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(!string.IsNullOrEmpty(user.PasswordHash));

    // --- IUserSecurityStampStore ---

    public Task SetSecurityStampAsync(ApplicationUser user, string stamp, CancellationToken ct) { user.SecurityStamp = stamp; return Task.CompletedTask; }
    public Task<string?> GetSecurityStampAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.SecurityStamp);

    // --- IUserLockoutStore ---

    public Task<DateTimeOffset?> GetLockoutEndDateAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.LockoutEnd);
    public Task SetLockoutEndDateAsync(ApplicationUser user, DateTimeOffset? lockoutEnd, CancellationToken ct) { user.LockoutEnd = lockoutEnd; return Task.CompletedTask; }
    public Task<int> IncrementAccessFailedCountAsync(ApplicationUser user, CancellationToken ct) { user.AccessFailedCount++; return Task.FromResult(user.AccessFailedCount); }
    public Task ResetAccessFailedCountAsync(ApplicationUser user, CancellationToken ct) { user.AccessFailedCount = 0; return Task.CompletedTask; }
    public Task<int> GetAccessFailedCountAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.AccessFailedCount);
    public Task<bool> GetLockoutEnabledAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.LockoutEnabled);
    public Task SetLockoutEnabledAsync(ApplicationUser user, bool enabled, CancellationToken ct) { user.LockoutEnabled = enabled; return Task.CompletedTask; }

    // --- IUserRoleStore ---

    public async Task AddToRoleAsync(ApplicationUser user, string normalizedRoleName, CancellationToken ct)
    {
        var role = await _roles.Find(r => r.NormalizedName == normalizedRoleName).FirstOrDefaultAsync(ct);
        if (role is null) return; // matches EF store behavior: caller is expected to have validated the role exists
        var already = await _userRoles.Find(ur => ur.UserId == user.Id && ur.RoleId == role.Id).AnyAsync(ct);
        if (!already)
        {
            await _userRoles.InsertOneAsync(new MongoUserRole { UserId = user.Id, RoleId = role.Id }, cancellationToken: ct);
        }
    }

    public async Task RemoveFromRoleAsync(ApplicationUser user, string normalizedRoleName, CancellationToken ct)
    {
        var role = await _roles.Find(r => r.NormalizedName == normalizedRoleName).FirstOrDefaultAsync(ct);
        if (role is null) return;
        await _userRoles.DeleteOneAsync(ur => ur.UserId == user.Id && ur.RoleId == role.Id, ct);
    }

    public async Task<IList<string>> GetRolesAsync(ApplicationUser user, CancellationToken ct)
    {
        var roleIds = await _userRoles.Find(ur => ur.UserId == user.Id).ToListAsync(ct);
        if (roleIds.Count == 0) return new List<string>();
        var ids = roleIds.Select(ur => ur.RoleId).ToList();
        var roles = await _roles.Find(r => ids.Contains(r.Id)).ToListAsync(ct);
        return roles.Select(r => r.Name!).ToList();
    }

    public async Task<bool> IsInRoleAsync(ApplicationUser user, string normalizedRoleName, CancellationToken ct)
    {
        var role = await _roles.Find(r => r.NormalizedName == normalizedRoleName).FirstOrDefaultAsync(ct);
        if (role is null) return false;
        return await _userRoles.Find(ur => ur.UserId == user.Id && ur.RoleId == role.Id).AnyAsync(ct);
    }

    public async Task<IList<ApplicationUser>> GetUsersInRoleAsync(string normalizedRoleName, CancellationToken ct)
    {
        var role = await _roles.Find(r => r.NormalizedName == normalizedRoleName).FirstOrDefaultAsync(ct);
        if (role is null) return new List<ApplicationUser>();
        var userIds = (await _userRoles.Find(ur => ur.RoleId == role.Id).ToListAsync(ct)).Select(ur => ur.UserId).ToList();
        if (userIds.Count == 0) return new List<ApplicationUser>();
        return await _users.Find(u => userIds.Contains(u.Id)).ToListAsync(ct);
    }

    public void Dispose() { }
}
