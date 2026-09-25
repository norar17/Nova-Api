using ECommerce.Domain.Entities;
using MongoDB.Driver;

namespace ECommerce.Infrastructure.Persistence.Mongo;

/// <summary>
/// Replaces ApplicationDbContext (the old IdentityDbContext&lt;...&gt; over Npgsql). Holds the
/// IMongoDatabase handle and exposes one IMongoCollection&lt;T&gt; per entity, mirroring the old
/// DbSet&lt;T&gt; properties one-for-one so callers only need to swap the type they inject.
///
/// Unlike EF's DbContext, this class has no change tracker and no SaveChangesAsync — every write
/// made through Repository&lt;T&gt; commits to Mongo immediately (see Repository.cs for what that
/// means for multi-step operations that used to be one transaction, e.g. checkout).
/// </summary>
public class MongoDbContext
{
    public IMongoDatabase Database { get; }

    public MongoDbContext(IMongoClient client, MongoOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.DatabaseName))
        {
            throw new InvalidOperationException("MongoDb:DatabaseName is not configured.");
        }

        Database = client.GetDatabase(options.DatabaseName);
    }

    public IMongoCollection<T> Collection<T>() => Database.GetCollection<T>(CollectionName<T>());

    /// <summary>
    /// Collection name for a given entity type. Kept centralized so Repository&lt;T&gt; and the
    /// index-creation / seeding code always agree on names, mirroring the old ToTable(...) calls.
    /// </summary>
    public static string CollectionName<T>() => typeof(T).Name switch
    {
        nameof(Category) => "Categories",
        nameof(Brand) => "Brands",
        nameof(Store) => "Stores",
        nameof(Product) => "Products",
        nameof(ProductImage) => "ProductImages",
        nameof(ProductVariant) => "ProductVariants",
        nameof(ProductTag) => "ProductTags",
        nameof(Order) => "Orders",
        nameof(OrderItem) => "OrderItems",
        nameof(Payment) => "Payments",
        nameof(CartItem) => "CartItems",
        nameof(WishlistItem) => "WishlistItems",
        nameof(Review) => "Reviews",
        nameof(ReviewImage) => "ReviewImages",
        nameof(Address) => "Addresses",
        nameof(Coupon) => "Coupons",
        nameof(Discount) => "Discounts",
        nameof(AuditLog) => "AuditLogs",
        nameof(Notification) => "Notifications",
        nameof(RefreshToken) => "RefreshTokens",
        nameof(Conversation) => "Conversations",
        nameof(Message) => "Messages",
        _ => typeof(T).Name + "s"
    };

    // Identity collections (not part of IRepository<T> since ApplicationUser/ApplicationRole don't
    // inherit BaseEntity — accessed directly by MongoUserStore / MongoRoleStore instead).
    public IMongoCollection<ApplicationUser> Users => Database.GetCollection<ApplicationUser>("Users");
    public IMongoCollection<ApplicationRole> Roles => Database.GetCollection<ApplicationRole>("Roles");
}
