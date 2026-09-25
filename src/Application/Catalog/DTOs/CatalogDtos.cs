namespace ECommerce.Application.Catalog.DTOs;

// ---- Product ----

public record ProductListItemDto(
    Guid Id,
    string Name,
    string Slug,
    decimal Price,
    decimal? DiscountPrice,
    string? ThumbnailUrl,
    string CategoryName,
    string BrandName,
    Guid StoreId,
    string StoreName,
    double AverageRating,
    int ReviewCount,
    bool IsFeatured,
    bool IsTrending,
    int StockQuantity);

public record ProductImageDto(Guid Id, string Url, bool IsThumbnail, int DisplayOrder);

public record ProductVariantDto(
    Guid Id, string? Size, string? Color, string? ColorHex,
    string Sku, int StockQuantity, decimal PriceAdjustment, bool IsActive);

public record ProductDetailDto(
    Guid Id,
    string Name,
    string Slug,
    string Description,
    string? ShortDescription,
    decimal Price,
    decimal? DiscountPrice,
    string Sku,
    string? Barcode,
    int StockQuantity,
    decimal? Weight,
    Guid CategoryId,
    string CategoryName,
    Guid BrandId,
    string BrandName,
    Guid StoreId,
    string StoreName,
    string StoreSlug,
    bool IsFeatured,
    bool IsTrending,
    double AverageRating,
    int ReviewCount,
    int SalesCount,
    IReadOnlyList<ProductImageDto> Images,
    IReadOnlyList<ProductVariantDto> Variants,
    IReadOnlyList<string> Tags);

public record CreateProductRequest(
    string Name,
    string Description,
    string? ShortDescription,
    decimal Price,
    decimal? DiscountPrice,
    string Sku,
    string? Barcode,
    int StockQuantity,
    decimal? Weight,
    Guid CategoryId,
    Guid BrandId,
    Guid StoreId,
    bool IsFeatured,
    bool IsTrending,
    IReadOnlyList<string> Tags);

public record UpdateProductRequest(
    string Name,
    string Description,
    string? ShortDescription,
    decimal Price,
    decimal? DiscountPrice,
    string Sku,
    string? Barcode,
    int StockQuantity,
    decimal? Weight,
    Guid CategoryId,
    Guid BrandId,
    bool IsFeatured,
    bool IsTrending,
    bool IsActive,
    IReadOnlyList<string> Tags);

public record CreateProductVariantRequest(
    string? Size, string? Color, string? ColorHex, string Sku, int StockQuantity, decimal PriceAdjustment);

public record ProductQueryParams(
    string? Search = null,
    Guid? CategoryId = null,
    Guid? BrandId = null,
    Guid? StoreId = null,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    double? MinRating = null,
    bool? FeaturedOnly = null,
    bool? TrendingOnly = null,
    string SortBy = "newest", // newest | price_asc | price_desc | rating | popularity
    int PageNumber = 1,
    int PageSize = 20);

// ---- Category ----

public record CategoryDto(
    Guid Id, string Name, string Slug, string? Description, string? ImageUrl,
    Guid? ParentCategoryId, int DisplayOrder, bool IsActive, int ProductCount);

public record CreateCategoryRequest(string Name, string? Description, Guid? ParentCategoryId, int DisplayOrder);
public record UpdateCategoryRequest(string Name, string? Description, Guid? ParentCategoryId, int DisplayOrder, bool IsActive);

// ---- Brand ----

public record BrandDto(Guid Id, string Name, string Slug, string? LogoUrl, string? Description, bool IsActive, int ProductCount);
public record CreateBrandRequest(string Name, string? Description);
public record UpdateBrandRequest(string Name, string? Description, bool IsActive);
