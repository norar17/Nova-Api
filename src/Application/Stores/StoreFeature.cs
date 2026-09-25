using ECommerce.Domain.Entities;
using ECommerce.Domain.Enums;
using ECommerce.Domain.Interfaces;
using ECommerce.Shared;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using MongoDB.Driver.Linq;

namespace ECommerce.Application.Stores.DTOs
{
    public record StoreDto(
        Guid Id, string Name, string Slug, string? Description, string? LogoUrl, string? BannerUrl,
        StoreStatus Status, string? RejectionReason, double AverageRating, int TotalSales, int ProductCount,
        DateTime CreatedAtUtc);

    public record CreateStoreRequest(string Name, string? Description);
    public record UpdateStoreRequest(string Name, string? Description);
    public record RejectStoreRequest(string Reason);
}

namespace ECommerce.Application.Stores.Interfaces
{
    using ECommerce.Application.Stores.DTOs;

    public interface IStoreService
    {
        Task<Result<StoreDto>> GetMyStoreAsync(Guid userId, CancellationToken cancellationToken = default);
        Task<Result<StoreDto>> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
        Task<Result<StoreDto>> CreateAsync(Guid userId, CreateStoreRequest request, CancellationToken cancellationToken = default);
        Task<Result<StoreDto>> UpdateAsync(Guid userId, UpdateStoreRequest request, CancellationToken cancellationToken = default);
        Task<Result<bool>> OwnsStoreAsync(Guid userId, Guid storeId, CancellationToken cancellationToken = default);

        // Admin
        Task<Result<IReadOnlyList<StoreDto>>> GetAllAsync(StoreStatus? statusFilter, CancellationToken cancellationToken = default);
        Task<Result<StoreDto>> ApproveAsync(Guid storeId, CancellationToken cancellationToken = default);
        Task<Result<StoreDto>> RejectAsync(Guid storeId, RejectStoreRequest request, CancellationToken cancellationToken = default);
        Task<Result<StoreDto>> SuspendAsync(Guid storeId, CancellationToken cancellationToken = default);
    }
}

namespace ECommerce.Application.Stores.Validators
{
    using ECommerce.Application.Stores.DTOs;

    public class CreateStoreRequestValidator : AbstractValidator<CreateStoreRequest>
    {
        public CreateStoreRequestValidator()
        {
            RuleFor(x => x.Name).NotEmpty().MinimumLength(3).MaximumLength(150);
            RuleFor(x => x.Description).MaximumLength(1000);
        }
    }

    public class UpdateStoreRequestValidator : AbstractValidator<UpdateStoreRequest>
    {
        public UpdateStoreRequestValidator()
        {
            RuleFor(x => x.Name).NotEmpty().MinimumLength(3).MaximumLength(150);
            RuleFor(x => x.Description).MaximumLength(1000);
        }
    }
}

namespace ECommerce.Application.Stores.Services
{
    using ECommerce.Application.Stores.DTOs;
    using ECommerce.Application.Stores.Interfaces;

    public class StoreService : IStoreService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly UserManager<ApplicationUser> _userManager;

        public StoreService(IUnitOfWork unitOfWork, UserManager<ApplicationUser> userManager)
        {
            _unitOfWork = unitOfWork;
            _userManager = userManager;
        }

        public async Task<Result<StoreDto>> GetMyStoreAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var store = await _unitOfWork.Repository<Store>().Query()
                .FirstOrDefaultAsync(s => s.OwnerId == userId, cancellationToken);

            if (store is null)
            {
                return Result.Failure<StoreDto>("You don't have a store yet.", "NO_STORE");
            }

            var productCount = await _unitOfWork.Repository<Product>().Query()
                .CountAsync(p => p.StoreId == store.Id, cancellationToken);

            return Result.Success(ToDto(store, productCount));
        }

        public async Task<Result<StoreDto>> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
        {
            var store = await _unitOfWork.Repository<Store>().Query()
                .FirstOrDefaultAsync(s => s.Slug == slug, cancellationToken);

            if (store is null || store.Status != StoreStatus.Approved)
            {
                return Result.NotFound<StoreDto>("Store", slug);
            }

            var productCount = await _unitOfWork.Repository<Product>().Query()
                .CountAsync(p => p.StoreId == store.Id && p.IsActive, cancellationToken);

            return Result.Success(ToDto(store, productCount));
        }

        public async Task<Result<StoreDto>> CreateAsync(Guid userId, CreateStoreRequest request, CancellationToken cancellationToken = default)
        {
            var alreadyHasStore = await _unitOfWork.Repository<Store>().ExistsAsync(s => s.OwnerId == userId, cancellationToken);
            if (alreadyHasStore)
            {
                return Result.Failure<StoreDto>("You already have a store.", "STORE_EXISTS");
            }

            var store = new Store
            {
                OwnerId = userId,
                Name = request.Name,
                Slug = Slugify(request.Name),
                Description = request.Description,
                Status = StoreStatus.Pending
            };

            await _unitOfWork.Repository<Store>().AddAsync(store, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Grant the Merchant role immediately — they can manage their (pending) store right away,
            // but ProductService only surfaces their products publicly once an admin approves the store.
            var user = await _userManager.FindByIdAsync(userId.ToString());
            if (user is not null && !await _userManager.IsInRoleAsync(user, "Merchant"))
            {
                await _userManager.AddToRoleAsync(user, "Merchant");
            }

            return Result.Success(ToDto(store, 0));
        }

        public async Task<Result<StoreDto>> UpdateAsync(Guid userId, UpdateStoreRequest request, CancellationToken cancellationToken = default)
        {
            var store = await _unitOfWork.Repository<Store>().Query(asNoTracking: false)
                .FirstOrDefaultAsync(s => s.OwnerId == userId, cancellationToken);

            if (store is null)
            {
                return Result.Failure<StoreDto>("You don't have a store yet.", "NO_STORE");
            }

            store.Name = request.Name;
            store.Slug = Slugify(request.Name);
            store.Description = request.Description;

            _unitOfWork.Repository<Store>().Update(store);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var productCount = await _unitOfWork.Repository<Product>().Query().CountAsync(p => p.StoreId == store.Id, cancellationToken);
            return Result.Success(ToDto(store, productCount));
        }

        public async Task<Result<bool>> OwnsStoreAsync(Guid userId, Guid storeId, CancellationToken cancellationToken = default)
        {
            var owns = await _unitOfWork.Repository<Store>().ExistsAsync(s => s.Id == storeId && s.OwnerId == userId, cancellationToken);
            return Result.Success(owns);
        }

        public async Task<Result<IReadOnlyList<StoreDto>>> GetAllAsync(StoreStatus? statusFilter, CancellationToken cancellationToken = default)
        {
            var query = _unitOfWork.Repository<Store>().Query().AsQueryable();
            if (statusFilter.HasValue)
            {
                query = query.Where(s => s.Status == statusFilter);
            }

            var stores = await query.OrderByDescending(s => s.CreatedAtUtc).ToListAsync(cancellationToken);
            var dtos = new List<StoreDto>();
            foreach (var store in stores)
            {
                var productCount = await _unitOfWork.Repository<Product>().Query().CountAsync(p => p.StoreId == store.Id, cancellationToken);
                dtos.Add(ToDto(store, productCount));
            }

            return Result.Success<IReadOnlyList<StoreDto>>(dtos);
        }

        public async Task<Result<StoreDto>> ApproveAsync(Guid storeId, CancellationToken cancellationToken = default)
        {
            var store = await _unitOfWork.Repository<Store>().GetByIdAsync(storeId, cancellationToken);
            if (store is null) return Result.NotFound<StoreDto>("Store", storeId);

            store.Status = StoreStatus.Approved;
            store.RejectionReason = null;
            _unitOfWork.Repository<Store>().Update(store);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(ToDto(store, 0));
        }

        public async Task<Result<StoreDto>> RejectAsync(Guid storeId, RejectStoreRequest request, CancellationToken cancellationToken = default)
        {
            var store = await _unitOfWork.Repository<Store>().GetByIdAsync(storeId, cancellationToken);
            if (store is null) return Result.NotFound<StoreDto>("Store", storeId);

            store.Status = StoreStatus.Rejected;
            store.RejectionReason = request.Reason;
            _unitOfWork.Repository<Store>().Update(store);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(ToDto(store, 0));
        }

        public async Task<Result<StoreDto>> SuspendAsync(Guid storeId, CancellationToken cancellationToken = default)
        {
            var store = await _unitOfWork.Repository<Store>().GetByIdAsync(storeId, cancellationToken);
            if (store is null) return Result.NotFound<StoreDto>("Store", storeId);

            store.Status = StoreStatus.Suspended;
            _unitOfWork.Repository<Store>().Update(store);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(ToDto(store, 0));
        }

        private static StoreDto ToDto(Store s, int productCount) => new(
            s.Id, s.Name, s.Slug, s.Description, s.LogoUrl, s.BannerUrl,
            s.Status, s.RejectionReason, s.AverageRating, s.TotalSales, productCount, s.CreatedAtUtc);

        internal static string Slugify(string name)
        {
            var slug = name.ToLowerInvariant().Trim();
            slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[^a-z0-9\s-]", "");
            slug = System.Text.RegularExpressions.Regex.Replace(slug, @"\s+", "-");
            slug = System.Text.RegularExpressions.Regex.Replace(slug, @"-+", "-").Trim('-');
            return $"{slug}-{Guid.NewGuid().ToString("N")[..6]}";
        }
    }
}
