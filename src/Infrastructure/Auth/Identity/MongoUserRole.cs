using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ECommerce.Infrastructure.Auth.Identity;

/// <summary>
/// Mirrors what used to be the EF-generated "UserRoles" join table (IdentityUserRole&lt;Guid&gt;).
/// Kept as its own tiny Mongo collection rather than embedding role ids on ApplicationUser, so the
/// Domain entity doesn't need to know anything about how role membership is persisted.
/// </summary>
public class MongoUserRole
{
    [BsonId]
    public ObjectId Id { get; set; }
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
}
