using ECommerce.Domain.Entities;
using ECommerce.Domain.Enums;
using ECommerce.Domain.Interfaces;
using ECommerce.Shared;
using FluentValidation;
using MongoDB.Driver.Linq;

namespace ECommerce.Application.Coupons.DTOs
{
    public record CouponDto(
        Guid Id, string Code, string? Description, DiscountType Type, decimal Value,
        decimal? MinimumOrderAmount, decimal? MaximumDiscountAmount,
        int? UsageLimit, int UsageCount, int? PerUserLimit,
        DateTime StartsAtUtc, DateTime ExpiresAtUtc, CouponStatus Status);

    public record CreateCouponRequest(
        string Code, string? Description, DiscountType Type, decimal Value,
        decimal? MinimumOrderAmount, decimal? MaximumDiscountAmount,
        int? UsageLimit, int? PerUserLimit, DateTime StartsAtUtc, DateTime ExpiresAtUtc);

    public record UpdateCouponRequest(
        string? Description, decimal Value, decimal? MinimumOrderAmount, decimal? MaximumDiscountAmount,
        int? UsageLimit, int? PerUserLimit, DateTime ExpiresAtUtc, CouponStatus Status);
}

namespace ECommerce.Application.Coupons.Interfaces
{
    using ECommerce.Application.Coupons.DTOs;

    public interface ICouponService
    {
        Task<Result<IReadOnlyList<CouponDto>>> GetAllAsync(CancellationToken cancellationToken = default);
        Task<Result<CouponDto>> CreateAsync(CreateCouponRequest request, CancellationToken cancellationToken = default);
        Task<Result<CouponDto>> UpdateAsync(Guid id, UpdateCouponRequest request, CancellationToken cancellationToken = default);
        Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    }
}

namespace ECommerce.Application.Coupons.Validators
{
    using ECommerce.Application.Coupons.DTOs;

    public class CreateCouponRequestValidator : AbstractValidator<CreateCouponRequest>
    {
        public CreateCouponRequestValidator()
        {
            RuleFor(x => x.Code).NotEmpty().MaximumLength(32);
            RuleFor(x => x.Value).GreaterThan(0);
            RuleFor(x => x.ExpiresAtUtc).GreaterThan(x => x.StartsAtUtc);
        }
    }
}

namespace ECommerce.Application.Coupons.Services
{
    using ECommerce.Application.Coupons.DTOs;
    using ECommerce.Application.Coupons.Interfaces;

    public class CouponService : ICouponService
    {
        private readonly IUnitOfWork _unitOfWork;

        public CouponService(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

        public async Task<Result<IReadOnlyList<CouponDto>>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            var coupons = await _unitOfWork.Repository<Coupon>().Query()
                .OrderByDescending(c => c.CreatedAtUtc)
                .ToListAsync(cancellationToken);

            return Result.Success<IReadOnlyList<CouponDto>>(coupons.Select(ToDto).ToList());
        }

        public async Task<Result<CouponDto>> CreateAsync(CreateCouponRequest request, CancellationToken cancellationToken = default)
        {
            var code = request.Code.ToUpperInvariant();
            var taken = await _unitOfWork.Repository<Coupon>().ExistsAsync(c => c.Code == code, cancellationToken);
            if (taken)
            {
                return Result.Failure<CouponDto>("A coupon with this code already exists.", "CODE_TAKEN");
            }

            var coupon = new Coupon
            {
                Code = code,
                Description = request.Description,
                Type = request.Type,
                Value = request.Value,
                MinimumOrderAmount = request.MinimumOrderAmount,
                MaximumDiscountAmount = request.MaximumDiscountAmount,
                UsageLimit = request.UsageLimit,
                PerUserLimit = request.PerUserLimit,
                StartsAtUtc = request.StartsAtUtc,
                ExpiresAtUtc = request.ExpiresAtUtc,
                Status = CouponStatus.Active
            };

            await _unitOfWork.Repository<Coupon>().AddAsync(coupon, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(ToDto(coupon));
        }

        public async Task<Result<CouponDto>> UpdateAsync(Guid id, UpdateCouponRequest request, CancellationToken cancellationToken = default)
        {
            var coupon = await _unitOfWork.Repository<Coupon>().GetByIdAsync(id, cancellationToken);
            if (coupon is null)
            {
                return Result.NotFound<CouponDto>("Coupon", id);
            }

            coupon.Description = request.Description;
            coupon.Value = request.Value;
            coupon.MinimumOrderAmount = request.MinimumOrderAmount;
            coupon.MaximumDiscountAmount = request.MaximumDiscountAmount;
            coupon.UsageLimit = request.UsageLimit;
            coupon.PerUserLimit = request.PerUserLimit;
            coupon.ExpiresAtUtc = request.ExpiresAtUtc;
            coupon.Status = request.Status;

            _unitOfWork.Repository<Coupon>().Update(coupon);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(ToDto(coupon));
        }

        public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var coupon = await _unitOfWork.Repository<Coupon>().GetByIdAsync(id, cancellationToken);
            if (coupon is null)
            {
                return Result.NotFound("Coupon", id);
            }

            coupon.Status = CouponStatus.Disabled;
            _unitOfWork.Repository<Coupon>().Update(coupon);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }

        private static CouponDto ToDto(Coupon c) => new(
            c.Id, c.Code, c.Description, c.Type, c.Value, c.MinimumOrderAmount, c.MaximumDiscountAmount,
            c.UsageLimit, c.UsageCount, c.PerUserLimit, c.StartsAtUtc, c.ExpiresAtUtc, c.Status);
    }
}
