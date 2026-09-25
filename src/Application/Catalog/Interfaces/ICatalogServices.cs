using ECommerce.Application.Catalog.DTOs;
using ECommerce.Shared;

namespace ECommerce.Application.Catalog.Interfaces;

public interface IProductService
{
    Task<Result<PagedResult<ProductListItemDto>>> SearchAsync(ProductQueryParams query, CancellationToken cancellationToken = default);
    Task<Result<PagedResult<ProductListItemDto>>> GetByStoreAsync(Guid storeId, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task<Result<ProductDetailDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<ProductDetailDto>> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<ProductListItemDto>>> GetRelatedAsync(Guid productId, int take = 8, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<ProductListItemDto>>> GetFeaturedAsync(int take = 12, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<ProductListItemDto>>> GetTrendingAsync(int take = 12, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<ProductListItemDto>>> GetNewArrivalsAsync(int take = 12, CancellationToken cancellationToken = default);

    Task<Result<ProductDetailDto>> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken = default);
    Task<Result<ProductDetailDto>> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken cancellationToken = default);
    Task<Result> SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result> RestoreAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result> AddVariantAsync(Guid productId, CreateProductVariantRequest request, CancellationToken cancellationToken = default);
    Task<Result> AddImageAsync(Guid productId, Stream fileStream, string fileName, bool isThumbnail, CancellationToken cancellationToken = default);
    Task<Result> DeleteImageAsync(Guid productId, Guid imageId, CancellationToken cancellationToken = default);
    Task<Result> AdjustStockAsync(Guid productId, Guid? variantId, int delta, CancellationToken cancellationToken = default);
}

public interface ICategoryService
{
    Task<Result<IReadOnlyList<CategoryDto>>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Result<CategoryDto>> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken = default);
    Task<Result<CategoryDto>> UpdateAsync(Guid id, UpdateCategoryRequest request, CancellationToken cancellationToken = default);
    Task<Result> SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result> RestoreAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface IBrandService
{
    Task<Result<IReadOnlyList<BrandDto>>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Result<BrandDto>> CreateAsync(CreateBrandRequest request, CancellationToken cancellationToken = default);
    Task<Result<BrandDto>> UpdateAsync(Guid id, UpdateBrandRequest request, CancellationToken cancellationToken = default);
    Task<Result> SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result> RestoreAsync(Guid id, CancellationToken cancellationToken = default);
}
