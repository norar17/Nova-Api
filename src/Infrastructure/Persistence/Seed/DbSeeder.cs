using ECommerce.Domain.Entities;
using ECommerce.Domain.Enums;
using ECommerce.Infrastructure.Persistence.Mongo;
using Microsoft.AspNetCore.Identity;
using MongoDB.Driver;

namespace ECommerce.Infrastructure.Persistence.Seed;

/// <summary>
/// Idempotent database seeder. Safe to run on every startup — checks existence before inserting.
/// Rewritten against MongoDbContext: every write commits immediately (no context.SaveChangesAsync()
/// batching like the EF version had), and relations that used to be EF navigation properties
/// (Product.Category, Product.Images, etc.) are now just plain Guid foreign keys — nothing here
/// attaches related objects in memory, it only sets the *Id fields.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(
        MongoDbContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        string adminEmail,
        string adminPassword)
    {
        await SeedRolesAsync(roleManager);
        await SeedAdminAsync(userManager, adminEmail, adminPassword);
        await SeedCategoriesAsync(context);
        await SeedBrandsAsync(context);

        await SeedSampleMerchantsAndProductsAsync(context, userManager);
    }

    private static async Task SeedRolesAsync(RoleManager<ApplicationRole> roleManager)
    {
        string[] roles = { "Admin", "Merchant", "Customer" };
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new ApplicationRole
                {
                    Name = role,
                    Description = role switch
                    {
                        "Admin" => "Full platform administration access",
                        "Merchant" => "Can create and manage a store and its products",
                        _ => "Standard customer access"
                    }
                });
            }
        }
    }

    private static async Task SeedAdminAsync(
        UserManager<ApplicationUser> userManager, string adminEmail, string adminPassword)
    {
        if (await userManager.FindByEmailAsync(adminEmail) is not null)
        {
            return;
        }

        var admin = new ApplicationUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            EmailConfirmed = true,
            FirstName = "Platform",
            LastName = "Administrator",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        var result = await userManager.CreateAsync(admin, adminPassword);
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(admin, "Admin");
        }
    }

    private static async Task SeedCategoriesAsync(MongoDbContext context)
    {
        var categories = context.Collection<Category>();
        if (await categories.Find(FilterDefinition<Category>.Empty).AnyAsync())
        {
            return;
        }

        await categories.InsertManyAsync(new[]
        {
            new Category { Name = "Men's Fashion", Slug = "mens-fashion", DisplayOrder = 1 },
            new Category { Name = "Women's Fashion", Slug = "womens-fashion", DisplayOrder = 2 },
            new Category { Name = "Footwear", Slug = "footwear", DisplayOrder = 3 },
            new Category { Name = "Electronics", Slug = "electronics", DisplayOrder = 4 },
            new Category { Name = "Home & Living", Slug = "home-living", DisplayOrder = 5 },
            new Category { Name = "Beauty & Personal Care", Slug = "beauty-personal-care", DisplayOrder = 6 },
            new Category { Name = "Accessories", Slug = "accessories", DisplayOrder = 7 }
        });
    }

    private static async Task SeedBrandsAsync(MongoDbContext context)
    {
        var brands = context.Collection<Brand>();
        if (await brands.Find(FilterDefinition<Brand>.Empty).AnyAsync())
        {
            return;
        }

        await brands.InsertManyAsync(new[]
        {
            new Brand { Name = "Nova", Slug = "nova" },
            new Brand { Name = "Kinetic", Slug = "kinetic" },
            new Brand { Name = "Urban Edge", Slug = "urban-edge" },
            new Brand { Name = "Lumen", Slug = "lumen" },
            new Brand { Name = "Terrain", Slug = "terrain" }
        });
    }

    private static async Task SeedSampleMerchantsAndProductsAsync(
        MongoDbContext context, UserManager<ApplicationUser> userManager)
    {
        var storesCollection = context.Collection<Store>();
        if (await storesCollection.Find(FilterDefinition<Store>.Empty).AnyAsync())
        {
            return; // already seeded
        }

        var categories = (await context.Collection<Category>().Find(FilterDefinition<Category>.Empty).ToListAsync())
            .ToDictionary(c => c.Slug, c => c);
        var brands = (await context.Collection<Brand>().Find(FilterDefinition<Brand>.Empty).ToListAsync())
            .ToDictionary(b => b.Slug, b => b);

        var storeSeeds = new (string Email, string First, string Last, string StoreName, string StoreSlug, string Bio)[]
        {
            ("seller.threadhouse@example.com", "Mika", "Reyes", "ThreadHouse PH", "threadhouse-ph",
                "Everyday streetwear and basics, cut for the tropics."),
            ("seller.gadgetloop@example.com", "Ben", "Santos", "GadgetLoop", "gadgetloop",
                "Budget-friendly electronics and gadgets, tested before listing."),
            ("seller.homeandhearth@example.com", "Ana", "Cruz", "Home & Hearth Co.", "home-and-hearth-co",
                "Small-batch home goods — candles, ceramics, linens."),
        };

        var stores = new List<Store>();
        foreach (var seed in storeSeeds)
        {
            var user = await userManager.FindByEmailAsync(seed.Email);
            if (user is null)
            {
                user = new ApplicationUser
                {
                    UserName = seed.Email,
                    Email = seed.Email,
                    EmailConfirmed = true,
                    FirstName = seed.First,
                    LastName = seed.Last,
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow
                };

                // Demo-only password — anyone deploying this for real should reset it immediately.
                var result = await userManager.CreateAsync(user, "SellerDemo123!");
                if (!result.Succeeded)
                {
                    continue;
                }

                await userManager.AddToRoleAsync(user, "Merchant");
            }

            stores.Add(new Store
            {
                OwnerId = user.Id,
                Name = seed.StoreName,
                Slug = seed.StoreSlug,
                Description = seed.Bio,
                Status = StoreStatus.Approved, // pre-approved so the catalog isn't empty on first run
                LogoUrl = $"https://picsum.photos/seed/{seed.StoreSlug}-logo/200/200",
                AverageRating = Math.Round(4.3 + Random.Shared.NextDouble() * 0.6, 1),
                TotalSales = Random.Shared.Next(50, 800)
            });
        }

        if (stores.Count == 0)
        {
            return;
        }

        await storesCollection.InsertManyAsync(stores); // Ids are assigned client-side (Guid.NewGuid() in BaseEntity), so they're available immediately

        var threadhouse = stores[0];
        var gadgetloop = stores[1];
        var hearth = stores[2];

        var products = new List<Product>
        {
            MakeProduct("Everyday Crewneck Tee", "everyday-crewneck-tee",
                "A soft, breathable cotton crewneck built for daily wear. Pre-shrunk fabric, reinforced seams, true-to-size fit.",
                "Soft cotton crewneck, everyday fit.", 449, 349, "THH-TEE-001", 120, threadhouse,
                categories["mens-fashion"], brands["urban-edge"], true, true, new[] { "basics", "cotton", "unisex" }),

            MakeProduct("Relaxed Fit Denim Jacket", "relaxed-fit-denim-jacket",
                "Mid-weight washed denim jacket with a relaxed silhouette. Button front, chest pockets, works year-round.",
                "Washed denim, relaxed silhouette.", 1899, null, "THH-JKT-002", 45, threadhouse,
                categories["mens-fashion"], brands["urban-edge"], true, false, new[] { "denim", "outerwear" }),

            MakeProduct("High-Waist Wide Leg Trousers", "high-waist-wide-leg-trousers",
                "Flowy wide-leg trousers with a flattering high waist. Lightweight woven fabric, side pockets, hidden zip.",
                "High-waist, wide-leg, lightweight.", 1299, 999, "THH-TRS-003", 60, threadhouse,
                categories["womens-fashion"], brands["urban-edge"], false, true, new[] { "trousers", "office" }),

            MakeProduct("Oversized Hoodie — Sand", "oversized-hoodie-sand",
                "Heavyweight fleece hoodie in a boxy oversized cut. Kangaroo pocket, ribbed cuffs, garment-dyed for a soft worn-in feel.",
                "Heavyweight fleece, boxy fit.", 1599, null, "THH-HOD-004", 80, threadhouse,
                categories["womens-fashion"], brands["nova"], true, true, new[] { "hoodie", "streetwear" }),

            MakeProduct("Canvas Low-Top Sneakers", "canvas-low-top-sneakers",
                "Classic canvas low-tops with a vulcanized rubber sole. Breathable lining, reinforced toe cap.",
                "Canvas low-tops, rubber sole.", 1799, 1499, "THH-SHO-005", 90, threadhouse,
                categories["footwear"], brands["urban-edge"], true, false, new[] { "sneakers", "canvas" }),

            MakeProduct("Wireless Earbuds Pro", "wireless-earbuds-pro",
                "True wireless earbuds with active noise cancellation, 28-hour total battery life with the charging case, and IPX5 sweat resistance.",
                "ANC, 28h battery, IPX5.", 2499, 1999, "GDL-AUD-001", 150, gadgetloop,
                categories["electronics"], brands["kinetic"], true, true, new[] { "audio", "wireless", "anc" }),

            MakeProduct("65W GaN Fast Charger", "65w-gan-fast-charger",
                "Compact 65W GaN charger with 3 ports (2x USB-C, 1x USB-A). Charges a laptop and two phones simultaneously.",
                "65W GaN, 3-port fast charging.", 1299, null, "GDL-CHG-002", 200, gadgetloop,
                categories["electronics"], brands["kinetic"], false, true, new[] { "charger", "gan", "travel" }),

            MakeProduct("Smart Fitness Band", "smart-fitness-band",
                "Lightweight fitness tracker with heart-rate monitoring, sleep tracking, and 10-day battery life. Water resistant to 50m.",
                "Heart-rate, sleep, 10-day battery.", 1899, 1599, "GDL-WBL-003", 110, gadgetloop,
                categories["electronics"], brands["lumen"], true, false, new[] { "wearable", "fitness" }),

            MakeProduct("Mechanical Keyboard — Compact 65%", "mechanical-keyboard-compact-65",
                "Hot-swappable compact mechanical keyboard with per-key RGB, PBT keycaps, and a detachable USB-C cable.",
                "Hot-swap, RGB, PBT keycaps.", 3499, 2999, "GDL-KBD-004", 40, gadgetloop,
                categories["electronics"], brands["kinetic"], true, true, new[] { "keyboard", "mechanical", "rgb" }),

            MakeProduct("Portable Bluetooth Speaker", "portable-bluetooth-speaker",
                "Rugged IP67 waterproof speaker with 12 hours of playtime and surprisingly deep bass for its size.",
                "IP67, 12h playtime, deep bass.", 1699, null, "GDL-SPK-005", 75, gadgetloop,
                categories["electronics"], brands["lumen"], false, false, new[] { "speaker", "bluetooth", "outdoor" }),

            MakeProduct("Soy Wax Candle — Sandalwood", "soy-wax-candle-sandalwood",
                "Hand-poured soy wax candle in a reusable ceramic vessel. 45-hour burn time, warm sandalwood and amber scent.",
                "Hand-poured soy wax, 45h burn.", 599, null, "HH-CND-001", 200, hearth,
                categories["home-living"], brands["terrain"], true, true, new[] { "candle", "home-fragrance" }),

            MakeProduct("Stoneware Dinner Set (4pc)", "stoneware-dinner-set-4pc",
                "Matte-glazed stoneware dinner set for four — plates, bowls, and mugs in an earthy, speckled finish.",
                "Matte stoneware, service for four.", 2299, 1899, "HH-DIN-002", 35, hearth,
                categories["home-living"], brands["terrain"], true, false, new[] { "dinnerware", "ceramic" }),

            MakeProduct("Linen Throw Blanket", "linen-throw-blanket",
                "Pre-washed 100% linen throw, breathable and soft with a relaxed drape. Machine washable.",
                "100% linen, pre-washed, breathable.", 1499, 1199, "HH-THR-003", 55, hearth,
                categories["home-living"], brands["terrain"], false, true, new[] { "linen", "throw", "textile" }),

            MakeProduct("Rattan Storage Basket Set", "rattan-storage-basket-set",
                "Set of 3 hand-woven rattan baskets in graduated sizes — great for shelving, laundry, or plant pots.",
                "Set of 3, hand-woven rattan.", 1099, null, "HH-BSK-004", 65, hearth,
                categories["home-living"], brands["terrain"], false, false, new[] { "storage", "rattan", "decor" }),
        };

        await context.Collection<Product>().InsertManyAsync(products);

        // Tags used to be a Product.Tags navigation collection populated by EF's HasMany(...).
        // They're their own top-level collection now, linked by ProductId.
        var tags = products.SelectMany(p => p.Tags.Select(t => { t.ProductId = p.Id; return t; })).ToList();
        if (tags.Count > 0)
        {
            await context.Collection<ProductTag>().InsertManyAsync(tags);
        }

        var images = new List<ProductImage>();
        foreach (var product in products)
        {
            images.Add(new ProductImage
            {
                ProductId = product.Id,
                Url = $"https://picsum.photos/seed/{product.Slug}/800/800",
                PublicId = $"seed/{product.Slug}", // not a real Cloudinary asset — seed images bypass upload
                IsThumbnail = true,
                DisplayOrder = 0
            });

            images.Add(new ProductImage
            {
                ProductId = product.Id,
                Url = $"https://picsum.photos/seed/{product.Slug}-alt/800/800",
                PublicId = $"seed/{product.Slug}-alt",
                IsThumbnail = false,
                DisplayOrder = 1
            });
        }

        await context.Collection<ProductImage>().InsertManyAsync(images);
    }

    private static Product MakeProduct(
        string name, string slug, string description, string shortDescription,
        decimal price, decimal? discountPrice, string sku, int stock, Store store,
        Category category, Brand brand, bool featured, bool trending, string[] tags) => new()
    {
        Name = name,
        Slug = slug,
        Description = description,
        ShortDescription = shortDescription,
        Price = price,
        DiscountPrice = discountPrice,
        Sku = sku,
        StockQuantity = stock,
        StoreId = store.Id,
        CategoryId = category.Id,
        BrandId = brand.Id,
        IsFeatured = featured,
        IsTrending = trending,
        IsActive = true,
        AverageRating = Math.Round(3.8 + Random.Shared.NextDouble() * 1.2, 1),
        ReviewCount = Random.Shared.Next(3, 240),
        SalesCount = Random.Shared.Next(5, 500),
        // ProductId gets filled in above once the product's own Id is known — Tags is a plain
        // in-memory list here (BsonIgnore'd on Product itself), used only to carry these through
        // to the separate ProductTags collection insert.
        Tags = tags.Select(t => new ProductTag { Name = t }).ToList()
    };
}
