using ECommerce.Domain.Entities;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace ECommerce.Infrastructure.Persistence.Mongo;

/// <summary>
/// Replaces the old Infrastructure/Persistence/Configurations/*.cs (EF Fluent API). There is no
/// direct Mongo equivalent of EF's HasOne/HasMany/Include relationship mapping, because Mongo
/// documents don't join. Every navigation property below (e.g. Product.Category, Order.Items) is
/// explicitly unmapped — only the plain scalar foreign-key fields (Product.CategoryId, etc.) are
/// persisted. Application code that used to rely on EF populating those navigation properties via
/// .Include(...) now has to fetch the related documents itself; see the services that were touched
/// for the Mongo conversion for the pattern used.
/// </summary>
public static class BsonMappings
{
    private static bool _registered;
    private static readonly object Lock = new();

    public static void Register()
    {
        if (_registered) return;

        lock (Lock)
        {
            if (_registered) return;

            // Store Guid as the standard 128-bit UUID representation (interoperable with other
            // drivers/tools), instead of the legacy .NET-specific byte order.
            BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));

            MapIgnoringNav<Category>("ParentCategory", "SubCategories", "Products");
            MapIgnoringNav<Brand>("Products");
            MapIgnoringNav<Store>("Owner", "Products");
            MapIgnoringNav<Product>("Category", "Brand", "Store", "Images", "Variants", "Tags",
                "Reviews", "OrderItems", "CartItems", "WishlistItems");
            MapIgnoringNav<ProductImage>("Product");
            MapIgnoringNav<ProductVariant>("Product", "CartItems", "OrderItems");
            MapIgnoringNav<ProductTag>("Product");

            MapIgnoringNav<Address>("User");
            MapIgnoringNav<Order>("User", "Coupon", "Items", "Payments");
            MapIgnoringNav<OrderItem>("Order", "Product", "ProductVariant");
            MapIgnoringNav<Payment>("Order");

            MapIgnoringNav<CartItem>("User", "Product", "ProductVariant");
            MapIgnoringNav<WishlistItem>("User", "Product");
            MapIgnoringNav<Review>("Product", "User", "Images");
            MapIgnoringNav<ReviewImage>("Review");
            MapIgnoringNav<Coupon>("Orders");
            MapIgnoringNav<Discount>("Category", "Product");
            MapIgnoringNav<AuditLog>("User");
            MapIgnoringNav<Notification>("User");
            MapIgnoringNav<RefreshToken>("User");

            MapIgnoringNav<Conversation>("Buyer", "Store", "Messages");
            MapIgnoringNav<Message>("Conversation", "Sender");

            // Identity entities: ApplicationUser/ApplicationRole aren't BaseEntity (they come from
            // IdentityUser<Guid>/IdentityRole<Guid>), so they're mapped separately here.
            MapIgnoringNav<ApplicationUser>("RefreshTokens", "Addresses", "Orders", "CartItems",
                "WishlistItems", "Reviews", "Notifications", "Stores");
            if (!BsonClassMap.IsClassMapRegistered(typeof(ApplicationRole)))
            {
                BsonClassMap.RegisterClassMap<ApplicationRole>(cm => cm.AutoMap());
            }

            _registered = true;
        }
    }

    private static void MapIgnoringNav<T>(params string[] propertiesToIgnore)
    {
        if (BsonClassMap.IsClassMapRegistered(typeof(T))) return;

        BsonClassMap.RegisterClassMap<T>(cm =>
        {
            cm.AutoMap();
            foreach (var prop in propertiesToIgnore)
            {
                cm.UnmapMember(typeof(T).GetProperty(prop)!);
            }
        });
    }

    /// <summary>
    /// Creates the unique/lookup indexes that used to come from HasIndex(...).IsUnique() in the EF
    /// configurations. Safe to call on every startup — CreateOneAsync is idempotent for an
    /// already-existing equivalent index.
    /// </summary>
    public static async Task EnsureIndexesAsync(MongoDbContext context, CancellationToken ct = default)
    {
        await CreateUniqueIndex(context.Collection<Category>(), "Slug", ct);
        await CreateUniqueIndex(context.Collection<Brand>(), "Slug", ct);
        await CreateUniqueIndex(context.Collection<Store>(), "Slug", ct);
        await CreateUniqueIndex(context.Collection<Store>(), "OwnerId", ct);
        await CreateUniqueIndex(context.Collection<Product>(), "Slug", ct);
        await CreateUniqueIndex(context.Collection<Product>(), "Sku", ct);
        await CreateUniqueIndex(context.Collection<ProductVariant>(), "Sku", ct);
        await CreateUniqueIndex(context.Collection<Coupon>(), "Code", ct);
        await CreateUniqueIndex(context.Collection<Payment>(), "StripePaymentIntentId", ct);
        await CreateUniqueIndex(context.Collection<RefreshToken>(), "TokenHash", ct);
        await CreateUniqueIndex(context.Collection<Order>(), "OrderNumber", ct);

        await CreateCompoundUniqueIndex(context.Collection<CartItem>(), ct, "UserId", "ProductId", "ProductVariantId");
        await CreateCompoundUniqueIndex(context.Collection<WishlistItem>(), ct, "UserId", "ProductId");
        await CreateCompoundUniqueIndex(context.Collection<Review>(), ct, "ProductId", "UserId");
        await CreateCompoundUniqueIndex(context.Collection<Conversation>(), ct, "BuyerId", "StoreId");

        var users = context.Users;
        await users.Indexes.CreateOneAsync(new MongoDB.Driver.CreateIndexModel<ApplicationUser>(
            MongoDB.Driver.Builders<ApplicationUser>.IndexKeys.Ascending("NormalizedEmail"),
            new MongoDB.Driver.CreateIndexOptions { Unique = true, Sparse = true }), cancellationToken: ct);
        await users.Indexes.CreateOneAsync(new MongoDB.Driver.CreateIndexModel<ApplicationUser>(
            MongoDB.Driver.Builders<ApplicationUser>.IndexKeys.Ascending("NormalizedUserName"),
            new MongoDB.Driver.CreateIndexOptions { Unique = true, Sparse = true }), cancellationToken: ct);

        var roles = context.Roles;
        await roles.Indexes.CreateOneAsync(new MongoDB.Driver.CreateIndexModel<ApplicationRole>(
            MongoDB.Driver.Builders<ApplicationRole>.IndexKeys.Ascending("NormalizedName"),
            new MongoDB.Driver.CreateIndexOptions { Unique = true, Sparse = true }), cancellationToken: ct);
    }

    private static Task CreateUniqueIndex<T>(MongoDB.Driver.IMongoCollection<T> collection, string field, CancellationToken ct) =>
        collection.Indexes.CreateOneAsync(new MongoDB.Driver.CreateIndexModel<T>(
            MongoDB.Driver.Builders<T>.IndexKeys.Ascending(field),
            new MongoDB.Driver.CreateIndexOptions { Unique = true }), cancellationToken: ct);

    private static Task CreateCompoundUniqueIndex<T>(MongoDB.Driver.IMongoCollection<T> collection, CancellationToken ct, params string[] fields)
    {
        var keysBuilder = MongoDB.Driver.Builders<T>.IndexKeys;
        MongoDB.Driver.IndexKeysDefinition<T> keys = keysBuilder.Ascending(fields[0]);
        for (var i = 1; i < fields.Length; i++)
        {
            keys = keysBuilder.Combine(keys, keysBuilder.Ascending(fields[i]));
        }

        return collection.Indexes.CreateOneAsync(new MongoDB.Driver.CreateIndexModel<T>(
            keys, new MongoDB.Driver.CreateIndexOptions { Unique = true }), cancellationToken: ct);
    }
}
