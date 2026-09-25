using ECommerce.Application.Catalog.DTOs;
using ECommerce.Application.Catalog.Interfaces;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Interfaces;
using ECommerce.Shared;
using MongoDB.Driver.Linq;

namespace ECommerce.Application.Catalog.Services;

public class CategoryService : ICategoryService
{
    private readonly IUnitOfWork _unitOfWork;

    public CategoryService(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<IReadOnlyList<CategoryDto>>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        // `!c.IsDeleted` is now explicit — there's no global soft-delete query filter on Mongo
        // queries (EF's HasQueryFilter has no equivalent here).
        var categories = await _unitOfWork.Repository<Category>().Query()
            .Where(c => !c.IsDeleted)
            .OrderBy(c => c.DisplayOrder)
            .ToListAsync(cancellationToken);

        // Replaces `c.Products.Count(p => p.IsActive)`: Category.Products isn't a persisted
        // navigation, so count active products per category directly from the Products collection.
        var categoryIds = categories.Select(c => c.Id).ToList();
        var activeProductCounts = (await _unitOfWork.Repository<Product>()
                .FindAsync(p => categoryIds.Contains(p.CategoryId) && p.IsActive, cancellationToken))
            .GroupBy(p => p.CategoryId)
            .ToDictionary(g => g.Key, g => g.Count());

        var dtos = categories.Select(c => new CategoryDto(
            c.Id, c.Name, c.Slug, c.Description, c.ImageUrl, c.ParentCategoryId,
            c.DisplayOrder, c.IsActive, activeProductCounts.GetValueOrDefault(c.Id))).ToList();

        return Result.Success<IReadOnlyList<CategoryDto>>(dtos);
    }

    public async Task<Result<CategoryDto>> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken = default)
    {
        var slug = Slugify(request.Name);
        var taken = await _unitOfWork.Repository<Category>().ExistsAsync(c => c.Slug == slug, cancellationToken);
        if (taken)
        {
            return Result.Failure<CategoryDto>("A category with this name already exists.", "NAME_TAKEN");
        }

        var category = new Category
        {
            Name = request.Name,
            Slug = slug,
            Description = request.Description,
            ParentCategoryId = request.ParentCategoryId,
            DisplayOrder = request.DisplayOrder,
            IsActive = true
        };

        await _unitOfWork.Repository<Category>().AddAsync(category, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new CategoryDto(category.Id, category.Name, category.Slug, category.Description,
            category.ImageUrl, category.ParentCategoryId, category.DisplayOrder, category.IsActive, 0));
    }

    public async Task<Result<CategoryDto>> UpdateAsync(Guid id, UpdateCategoryRequest request, CancellationToken cancellationToken = default)
    {
        var category = await _unitOfWork.Repository<Category>().GetByIdAsync(id, cancellationToken);
        if (category is null)
        {
            return Result.NotFound<CategoryDto>("Category", id);
        }

        category.Name = request.Name;
        category.Slug = Slugify(request.Name);
        category.Description = request.Description;
        category.ParentCategoryId = request.ParentCategoryId;
        category.DisplayOrder = request.DisplayOrder;
        category.IsActive = request.IsActive;

        _unitOfWork.Repository<Category>().Update(category);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new CategoryDto(category.Id, category.Name, category.Slug, category.Description,
            category.ImageUrl, category.ParentCategoryId, category.DisplayOrder, category.IsActive, 0));
    }

    public async Task<Result> SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var category = await _unitOfWork.Repository<Category>().GetByIdAsync(id, cancellationToken);
        if (category is null) return Result.NotFound("Category", id);

        _unitOfWork.Repository<Category>().SoftDelete(category);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // No IgnoreQueryFilters() needed: there's no global soft-delete filter on Mongo queries,
        // so a soft-deleted category is already reachable via a plain lookup.
        var category = await _unitOfWork.Repository<Category>().GetByIdAsync(id, cancellationToken);
        if (category is null) return Result.NotFound("Category", id);

        category.IsDeleted = false;
        category.DeletedAtUtc = null;
        _unitOfWork.Repository<Category>().Update(category);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    internal static string Slugify(string name)
    {
        var slug = name.ToLowerInvariant().Trim();
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"\s+", "-");
        return System.Text.RegularExpressions.Regex.Replace(slug, @"-+", "-").Trim('-');
    }
}

public class BrandService : IBrandService
{
    private readonly IUnitOfWork _unitOfWork;

    public BrandService(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<IReadOnlyList<BrandDto>>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var brands = await _unitOfWork.Repository<Brand>().Query()
            .Where(b => !b.IsDeleted)
            .OrderBy(b => b.Name)
            .ToListAsync(cancellationToken);

        var brandIds = brands.Select(b => b.Id).ToList();
        var activeProductCounts = (await _unitOfWork.Repository<Product>()
                .FindAsync(p => brandIds.Contains(p.BrandId) && p.IsActive, cancellationToken))
            .GroupBy(p => p.BrandId)
            .ToDictionary(g => g.Key, g => g.Count());

        var dtos = brands.Select(b => new BrandDto(
            b.Id, b.Name, b.Slug, b.LogoUrl, b.Description, b.IsActive, activeProductCounts.GetValueOrDefault(b.Id))).ToList();

        return Result.Success<IReadOnlyList<BrandDto>>(dtos);
    }

    public async Task<Result<BrandDto>> CreateAsync(CreateBrandRequest request, CancellationToken cancellationToken = default)
    {
        var slug = CategoryService.Slugify(request.Name);
        var taken = await _unitOfWork.Repository<Brand>().ExistsAsync(b => b.Slug == slug, cancellationToken);
        if (taken)
        {
            return Result.Failure<BrandDto>("A brand with this name already exists.", "NAME_TAKEN");
        }

        var brand = new Brand { Name = request.Name, Slug = slug, Description = request.Description, IsActive = true };
        await _unitOfWork.Repository<Brand>().AddAsync(brand, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new BrandDto(brand.Id, brand.Name, brand.Slug, brand.LogoUrl, brand.Description, brand.IsActive, 0));
    }

    public async Task<Result<BrandDto>> UpdateAsync(Guid id, UpdateBrandRequest request, CancellationToken cancellationToken = default)
    {
        var brand = await _unitOfWork.Repository<Brand>().GetByIdAsync(id, cancellationToken);
        if (brand is null) return Result.NotFound<BrandDto>("Brand", id);

        brand.Name = request.Name;
        brand.Slug = CategoryService.Slugify(request.Name);
        brand.Description = request.Description;
        brand.IsActive = request.IsActive;

        _unitOfWork.Repository<Brand>().Update(brand);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new BrandDto(brand.Id, brand.Name, brand.Slug, brand.LogoUrl, brand.Description, brand.IsActive, 0));
    }

    public async Task<Result> SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var brand = await _unitOfWork.Repository<Brand>().GetByIdAsync(id, cancellationToken);
        if (brand is null) return Result.NotFound("Brand", id);

        _unitOfWork.Repository<Brand>().SoftDelete(brand);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var brand = await _unitOfWork.Repository<Brand>().GetByIdAsync(id, cancellationToken);
        if (brand is null) return Result.NotFound("Brand", id);

        brand.IsDeleted = false;
        brand.DeletedAtUtc = null;
        _unitOfWork.Repository<Brand>().Update(brand);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
