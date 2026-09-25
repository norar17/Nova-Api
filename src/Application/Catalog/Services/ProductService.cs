using ECommerce.Application.Catalog.DTOs;
using ECommerce.Application.Catalog.Interfaces;
using ECommerce.Application.Common.Interfaces;
using MongoDB.Driver.Linq;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Enums;
using ECommerce.Domain.Interfaces;
using ECommerce.Shared;

namespace ECommerce.Application.Catalog.Services;

public class ProductService : IProductService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IImageStorageService _imageStorage;

    public ProductService(IUnitOfWork unitOfWork, IImageStorageService imageStorage)
    {
        _unitOfWork = unitOfWork;
        _imageStorage = imageStorage;
    }

    public async Task<Result<PagedResult<ProductListItemDto>>> SearchAsync(ProductQueryParams query, CancellationToken cancellationToken = default)
    {
        // `p.Store.Status == StoreStatus.Approved` used to be an EF join. Mongo can't join across
        // collections in a query, so resolve the approved store ids up front and filter by StoreId
        // instead (translates to a Mongo `$in`).
        var approvedStoreIds = (await _unitOfWork.Repository<Store>()
                .FindAsync(s => s.Status == StoreStatus.Approved, cancellationToken))
            .Select(s => s.Id).ToList();

        var products = _unitOfWork.Repository<Product>().Query()
            .Where(p => p.IsActive && !p.IsDeleted && approvedStoreIds.Contains(p.StoreId));

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();

            // Tag matches used to come from `p.Tags.Any(...)` against EF's joined collection. Tags
            // are their own top-level collection now, so resolve matching product ids first.
            var matchingTagProductIds = (await _unitOfWork.Repository<ProductTag>()
                    .FindAsync(t => t.Name.ToLower().Contains(term), cancellationToken))
                .Select(t => t.ProductId).Distinct().ToList();

            products = products.Where(p => p.Name.ToLower().Contains(term) || matchingTagProductIds.Contains(p.Id));
        }

        if (query.CategoryId.HasValue) products = products.Where(p => p.CategoryId == query.CategoryId);
        if (query.BrandId.HasValue) products = products.Where(p => p.BrandId == query.BrandId);
        if (query.StoreId.HasValue) products = products.Where(p => p.StoreId == query.StoreId);
        if (query.MinPrice.HasValue) products = products.Where(p => (p.DiscountPrice ?? p.Price) >= query.MinPrice);
        if (query.MaxPrice.HasValue) products = products.Where(p => (p.DiscountPrice ?? p.Price) <= query.MaxPrice);
        if (query.MinRating.HasValue) products = products.Where(p => p.AverageRating >= query.MinRating);
        if (query.FeaturedOnly == true) products = products.Where(p => p.IsFeatured);
        if (query.TrendingOnly == true) products = products.Where(p => p.IsTrending);

        products = query.SortBy switch
        {
            "price_asc" => products.OrderBy(p => p.DiscountPrice ?? p.Price),
            "price_desc" => products.OrderByDescending(p => p.DiscountPrice ?? p.Price),
            "rating" => products.OrderByDescending(p => p.AverageRating),
            "popularity" => products.OrderByDescending(p => p.SalesCount),
            _ => products.OrderByDescending(p => p.CreatedAtUtc)
        };

        var totalCount = await products.CountAsync(cancellationToken);

        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var pageNumber = Math.Max(query.PageNumber, 1);

        var entities = await products
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = await ToListItemDtosAsync(entities, cancellationToken);

        return Result.Success(new PagedResult<ProductListItemDto>
        {
            Items = items,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalCount = totalCount
        });
    }

    public async Task<Result<PagedResult<ProductListItemDto>>> GetByStoreAsync(Guid storeId, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        // No IgnoreQueryFilters() needed anymore: there's no global soft-delete filter on Mongo
        // queries (EF's HasQueryFilter has no equivalent here), so merchants already see their own
        // soft-deleted/inactive products without any extra opt-out call.
        var query = _unitOfWork.Repository<Product>().Query()
            .Where(p => p.StoreId == storeId)
            .OrderByDescending(p => p.CreatedAtUtc);

        pageSize = Math.Clamp(pageSize, 1, 100);
        pageNumber = Math.Max(pageNumber, 1);

        var totalCount = await query.CountAsync(cancellationToken);
        var entities = await query
            .Skip((pageNumber - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        var items = await ToListItemDtosAsync(entities, cancellationToken);

        return Result.Success(new PagedResult<ProductListItemDto>
        {
            Items = items,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalCount = totalCount
        });
    }

    public async Task<Result<ProductDetailDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await _unitOfWork.Repository<Product>().GetByIdAsync(id, cancellationToken);
        if (product is null || product.IsDeleted)
        {
            return Result.NotFound<ProductDetailDto>("Product", id);
        }

        await IncrementViewCountAsync(product, cancellationToken);

        return Result.Success(await ToDetailDtoAsync(product, cancellationToken));
    }

    public async Task<Result<ProductDetailDto>> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var product = await _unitOfWork.Repository<Product>().FindOneAsync(p => p.Slug == slug, cancellationToken);
        if (product is null || product.IsDeleted)
        {
            return Result.NotFound<ProductDetailDto>("Product", slug);
        }

        await IncrementViewCountAsync(product, cancellationToken);

        return Result.Success(await ToDetailDtoAsync(product, cancellationToken));
    }

    private async Task IncrementViewCountAsync(Product product, CancellationToken cancellationToken)
    {
        product.ViewCount++;
        await _unitOfWork.Repository<Product>().UpdateAsync(product, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<ProductListItemDto>>> GetRelatedAsync(Guid productId, int take = 8, CancellationToken cancellationToken = default)
    {
        var product = await _unitOfWork.Repository<Product>().GetByIdAsync(productId, cancellationToken);
        if (product is null)
        {
            return Result.NotFound<IReadOnlyList<ProductListItemDto>>("Product", productId);
        }

        var related = await _unitOfWork.Repository<Product>().Query()
            .Where(p => p.IsActive && !p.IsDeleted && p.CategoryId == product.CategoryId && p.Id != productId)
            .OrderByDescending(p => p.SalesCount)
            .Take(take)
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<ProductListItemDto>>(await ToListItemDtosAsync(related, cancellationToken));
    }

    public Task<Result<IReadOnlyList<ProductListItemDto>>> GetFeaturedAsync(int take = 12, CancellationToken cancellationToken = default) =>
        GetFlaggedAsync(p => p.IsFeatured, p => p.OrderByDescending(x => x.CreatedAtUtc), take, cancellationToken);

    public Task<Result<IReadOnlyList<ProductListItemDto>>> GetTrendingAsync(int take = 12, CancellationToken cancellationToken = default) =>
        GetFlaggedAsync(p => p.IsTrending, p => p.OrderByDescending(x => x.SalesCount), take, cancellationToken);

    public Task<Result<IReadOnlyList<ProductListItemDto>>> GetNewArrivalsAsync(int take = 12, CancellationToken cancellationToken = default) =>
        GetFlaggedAsync(p => p.IsActive, p => p.OrderByDescending(x => x.CreatedAtUtc), take, cancellationToken);

    private async Task<Result<IReadOnlyList<ProductListItemDto>>> GetFlaggedAsync(
        System.Linq.Expressions.Expression<Func<Product, bool>> filter,
        Func<IQueryable<Product>, IOrderedQueryable<Product>> order,
        int take, CancellationToken cancellationToken)
    {
        var approvedStoreIds = (await _unitOfWork.Repository<Store>()
                .FindAsync(s => s.Status == StoreStatus.Approved, cancellationToken))
            .Select(s => s.Id).ToList();

        var query = order(_unitOfWork.Repository<Product>().Query()
            .Where(p => p.IsActive && !p.IsDeleted && approvedStoreIds.Contains(p.StoreId))
            .Where(filter));

        var entities = await query.Take(take).ToListAsync(cancellationToken);
        var items = await ToListItemDtosAsync(entities, cancellationToken);
        return Result.Success<IReadOnlyList<ProductListItemDto>>(items);
    }

    public async Task<Result<ProductDetailDto>> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken = default)
    {
        var skuExists = await _unitOfWork.Repository<Product>().ExistsAsync(p => p.Sku == request.Sku, cancellationToken);
        if (skuExists)
        {
            return Result.Failure<ProductDetailDto>("A product with this SKU already exists.", "SKU_TAKEN");
        }

        var storeExists = await _unitOfWork.Repository<Store>().ExistsAsync(s => s.Id == request.StoreId, cancellationToken);
        if (!storeExists)
        {
            return Result.Failure<ProductDetailDto>("Store not found.", "STORE_NOT_FOUND");
        }

        var product = new Product
        {
            Name = request.Name,
            Slug = GenerateSlug(request.Name),
            Description = request.Description,
            ShortDescription = request.ShortDescription,
            Price = request.Price,
            DiscountPrice = request.DiscountPrice,
            Sku = request.Sku,
            Barcode = request.Barcode,
            StockQuantity = request.StockQuantity,
            Weight = request.Weight,
            CategoryId = request.CategoryId,
            BrandId = request.BrandId,
            StoreId = request.StoreId,
            IsFeatured = request.IsFeatured,
            IsTrending = request.IsTrending,
            IsActive = true
        };

        await _unitOfWork.Repository<Product>().AddAsync(product, cancellationToken);

        // Tags used to be cascade-inserted by EF from product.Tags. Product.Tags isn't persisted on
        // the document anymore (see BsonMappings) — write them to their own collection directly.
        foreach (var tagName in request.Tags)
        {
            await _unitOfWork.Repository<ProductTag>().AddAsync(
                new ProductTag { ProductId = product.Id, Name = tagName }, cancellationToken);
        }

        return Result.Success(await ToDetailDtoAsync(product, cancellationToken));
    }

    public async Task<Result<ProductDetailDto>> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken cancellationToken = default)
    {
        var product = await _unitOfWork.Repository<Product>().GetByIdAsync(id, cancellationToken);
        if (product is null)
        {
            return Result.NotFound<ProductDetailDto>("Product", id);
        }

        var skuTaken = await _unitOfWork.Repository<Product>()
            .ExistsAsync(p => p.Sku == request.Sku && p.Id != id, cancellationToken);
        if (skuTaken)
        {
            return Result.Failure<ProductDetailDto>("A product with this SKU already exists.", "SKU_TAKEN");
        }

        product.Name = request.Name;
        product.Slug = GenerateSlug(request.Name);
        product.Description = request.Description;
        product.ShortDescription = request.ShortDescription;
        product.Price = request.Price;
        product.DiscountPrice = request.DiscountPrice;
        product.Sku = request.Sku;
        product.Barcode = request.Barcode;
        product.StockQuantity = request.StockQuantity;
        product.Weight = request.Weight;
        product.CategoryId = request.CategoryId;
        product.BrandId = request.BrandId;
        product.IsFeatured = request.IsFeatured;
        product.IsTrending = request.IsTrending;
        product.IsActive = request.IsActive;

        await _unitOfWork.Repository<Product>().UpdateAsync(product, cancellationToken);

        // Replace the tag set: remove the old ProductTag documents for this product, insert the new ones.
        var existingTags = await _unitOfWork.Repository<ProductTag>().FindAsync(t => t.ProductId == id, cancellationToken);
        foreach (var tag in existingTags)
        {
            _unitOfWork.Repository<ProductTag>().Remove(tag);
        }
        foreach (var tagName in request.Tags)
        {
            await _unitOfWork.Repository<ProductTag>().AddAsync(
                new ProductTag { ProductId = id, Name = tagName }, cancellationToken);
        }

        return Result.Success(await ToDetailDtoAsync(product, cancellationToken));
    }

    public async Task<Result> SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await _unitOfWork.Repository<Product>().GetByIdAsync(id, cancellationToken);
        if (product is null)
        {
            return Result.NotFound("Product", id);
        }

        _unitOfWork.Repository<Product>().SoftDelete(product);
        return Result.Success();
    }

    public async Task<Result> RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // No IgnoreQueryFilters() needed: GetByIdAsync never filtered on IsDeleted in the Mongo
        // repository, so a soft-deleted product is already reachable here.
        var product = await _unitOfWork.Repository<Product>().GetByIdAsync(id, cancellationToken);

        if (product is null)
        {
            return Result.NotFound("Product", id);
        }

        product.IsDeleted = false;
        product.DeletedAtUtc = null;
        await _unitOfWork.Repository<Product>().UpdateAsync(product, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> AddVariantAsync(Guid productId, CreateProductVariantRequest request, CancellationToken cancellationToken = default)
    {
        var exists = await _unitOfWork.Repository<Product>().ExistsAsync(p => p.Id == productId, cancellationToken);
        if (!exists)
        {
            return Result.NotFound("Product", productId);
        }

        var skuTaken = await _unitOfWork.Repository<ProductVariant>().ExistsAsync(v => v.Sku == request.Sku, cancellationToken);
        if (skuTaken)
        {
            return Result.Failure("A variant with this SKU already exists.", "SKU_TAKEN");
        }

        var variant = new ProductVariant
        {
            ProductId = productId,
            Size = request.Size,
            Color = request.Color,
            ColorHex = request.ColorHex,
            Sku = request.Sku,
            StockQuantity = request.StockQuantity,
            PriceAdjustment = request.PriceAdjustment,
            IsActive = true
        };

        await _unitOfWork.Repository<ProductVariant>().AddAsync(variant, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> AddImageAsync(Guid productId, Stream fileStream, string fileName, bool isThumbnail, CancellationToken cancellationToken = default)
    {
        var exists = await _unitOfWork.Repository<Product>().ExistsAsync(p => p.Id == productId, cancellationToken);
        if (!exists)
        {
            return Result.NotFound("Product", productId);
        }

        var (url, publicId) = await _imageStorage.UploadAsync(fileStream, fileName, $"products/{productId}", cancellationToken);

        var image = new ProductImage
        {
            ProductId = productId,
            Url = url,
            PublicId = publicId,
            IsThumbnail = isThumbnail
        };

        await _unitOfWork.Repository<ProductImage>().AddAsync(image, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> DeleteImageAsync(Guid productId, Guid imageId, CancellationToken cancellationToken = default)
    {
        var image = await _unitOfWork.Repository<ProductImage>()
            .FindOneAsync(i => i.Id == imageId && i.ProductId == productId, cancellationToken);

        if (image is null)
        {
            return Result.NotFound("Product image", imageId);
        }

        await _imageStorage.DeleteAsync(image.PublicId, cancellationToken);
        _unitOfWork.Repository<ProductImage>().Remove(image);

        return Result.Success();
    }

    public async Task<Result> AdjustStockAsync(Guid productId, Guid? variantId, int delta, CancellationToken cancellationToken = default)
    {
        if (variantId.HasValue)
        {
            var variant = await _unitOfWork.Repository<ProductVariant>().GetByIdAsync(variantId.Value, cancellationToken);
            if (variant is null || variant.ProductId != productId)
            {
                return Result.NotFound("Product variant", variantId.Value);
            }

            if (variant.StockQuantity + delta < 0)
            {
                return Result.Failure("Insufficient stock for this variant.", "INSUFFICIENT_STOCK");
            }

            variant.StockQuantity += delta;
            _unitOfWork.Repository<ProductVariant>().Update(variant);
        }
        else
        {
            var product = await _unitOfWork.Repository<Product>().GetByIdAsync(productId, cancellationToken);
            if (product is null)
            {
                return Result.NotFound("Product", productId);
            }

            if (product.StockQuantity + delta < 0)
            {
                return Result.Failure("Insufficient stock.", "INSUFFICIENT_STOCK");
            }

            product.StockQuantity += delta;
            if (delta < 0) product.SalesCount += Math.Abs(delta);
            _unitOfWork.Repository<Product>().Update(product);
        }

        return Result.Success();
    }

    // ---- helpers ----

    /// <summary>
    /// Batch-resolves Category/Brand/Store/Images for a page of products in a handful of queries
    /// (one per related collection) instead of one round trip per product. This is the Mongo
    /// replacement for `.Include(p => p.Category).Include(p => p.Brand)...` — there's no server-side
    /// join, so the "join" happens here in application code via id lookups.
    /// </summary>
    private async Task<List<ProductListItemDto>> ToListItemDtosAsync(IReadOnlyList<Product> products, CancellationToken cancellationToken)
    {
        if (products.Count == 0) return new List<ProductListItemDto>();

        var categoryIds = products.Select(p => p.CategoryId).Distinct().ToList();
        var brandIds = products.Select(p => p.BrandId).Distinct().ToList();
        var storeIds = products.Select(p => p.StoreId).Distinct().ToList();
        var productIds = products.Select(p => p.Id).ToList();

        var categories = (await _unitOfWork.Repository<Category>().GetByIdsAsync(categoryIds, cancellationToken)).ToDictionary(c => c.Id);
        var brands = (await _unitOfWork.Repository<Brand>().GetByIdsAsync(brandIds, cancellationToken)).ToDictionary(b => b.Id);
        var stores = (await _unitOfWork.Repository<Store>().GetByIdsAsync(storeIds, cancellationToken)).ToDictionary(s => s.Id);
        var imagesByProduct = (await _unitOfWork.Repository<ProductImage>().FindAsync(i => productIds.Contains(i.ProductId), cancellationToken))
            .GroupBy(i => i.ProductId).ToDictionary(g => g.Key, g => g.ToList());

        return products.Select(p =>
        {
            imagesByProduct.TryGetValue(p.Id, out var images);
            var thumbnailUrl = images?.FirstOrDefault(i => i.IsThumbnail)?.Url ?? images?.FirstOrDefault()?.Url;
            categories.TryGetValue(p.CategoryId, out var category);
            brands.TryGetValue(p.BrandId, out var brand);
            stores.TryGetValue(p.StoreId, out var store);

            return new ProductListItemDto(
                p.Id, p.Name, p.Slug, p.Price, p.DiscountPrice, thumbnailUrl,
                category?.Name ?? string.Empty, brand?.Name ?? string.Empty, p.StoreId, store?.Name ?? string.Empty,
                p.AverageRating, p.ReviewCount, p.IsFeatured, p.IsTrending, p.StockQuantity);
        }).ToList();
    }

    private async Task<ProductDetailDto> ToDetailDtoAsync(Product p, CancellationToken cancellationToken)
    {
        var category = await _unitOfWork.Repository<Category>().GetByIdAsync(p.CategoryId, cancellationToken);
        var brand = await _unitOfWork.Repository<Brand>().GetByIdAsync(p.BrandId, cancellationToken);
        var store = await _unitOfWork.Repository<Store>().GetByIdAsync(p.StoreId, cancellationToken);
        var images = await _unitOfWork.Repository<ProductImage>().FindAsync(i => i.ProductId == p.Id, cancellationToken);
        var variants = await _unitOfWork.Repository<ProductVariant>().FindAsync(v => v.ProductId == p.Id, cancellationToken);
        var tags = await _unitOfWork.Repository<ProductTag>().FindAsync(t => t.ProductId == p.Id, cancellationToken);

        return new ProductDetailDto(
            p.Id, p.Name, p.Slug, p.Description, p.ShortDescription, p.Price, p.DiscountPrice,
            p.Sku, p.Barcode, p.StockQuantity, p.Weight, p.CategoryId, category?.Name ?? string.Empty, p.BrandId, brand?.Name ?? string.Empty,
            p.StoreId, store?.Name ?? string.Empty, store?.Slug ?? string.Empty,
            p.IsFeatured, p.IsTrending, p.AverageRating, p.ReviewCount, p.SalesCount,
            images.OrderBy(i => i.DisplayOrder).Select(i => new ProductImageDto(i.Id, i.Url, i.IsThumbnail, i.DisplayOrder)).ToList(),
            variants.Select(v => new ProductVariantDto(v.Id, v.Size, v.Color, v.ColorHex, v.Sku, v.StockQuantity, v.PriceAdjustment, v.IsActive)).ToList(),
            tags.Select(t => t.Name).ToList());
    }

    private static string GenerateSlug(string name)
    {
        var slug = name.ToLowerInvariant().Trim();
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"\s+", "-");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"-+", "-").Trim('-');
        return $"{slug}-{Guid.NewGuid().ToString("N")[..6]}"; // suffix guarantees uniqueness without a DB round-trip
    }
}
