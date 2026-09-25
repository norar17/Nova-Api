using MongoDB.Driver.Linq;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Enums;
using ECommerce.Domain.Interfaces;
using ECommerce.Shared;
using FluentValidation;

namespace ECommerce.Application.Reviews.DTOs
{
    public record ReviewDto(
        Guid Id, Guid UserId, string UserName, string? UserAvatarUrl,
        int Rating, string? Title, string Comment, bool IsVerifiedPurchase,
        int HelpfulCount, IReadOnlyList<string> ImageUrls, DateTime CreatedAtUtc);

    public record CreateReviewRequest(int Rating, string? Title, string Comment);
}

namespace ECommerce.Application.Reviews.Interfaces
{
    using ECommerce.Application.Reviews.DTOs;

    public interface IReviewService
    {
        Task<Result<PagedResult<ReviewDto>>> GetForProductAsync(Guid productId, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
        Task<Result<ReviewDto>> CreateAsync(Guid userId, Guid productId, CreateReviewRequest request, CancellationToken cancellationToken = default);
        Task<Result> DeleteAsync(Guid userId, Guid reviewId, bool isAdmin, CancellationToken cancellationToken = default);
        Task<Result> MarkHelpfulAsync(Guid reviewId, CancellationToken cancellationToken = default);
    }
}

namespace ECommerce.Application.Reviews.Validators
{
    using ECommerce.Application.Reviews.DTOs;

    public class CreateReviewRequestValidator : AbstractValidator<CreateReviewRequest>
    {
        public CreateReviewRequestValidator()
        {
            RuleFor(x => x.Rating).InclusiveBetween(1, 5);
            RuleFor(x => x.Title).MaximumLength(150);
            RuleFor(x => x.Comment).NotEmpty().MaximumLength(2000);
        }
    }
}

namespace ECommerce.Application.Reviews.Services
{
    using ECommerce.Application.Reviews.DTOs;
    using ECommerce.Application.Reviews.Interfaces;

    public class ReviewService : IReviewService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly Microsoft.AspNetCore.Identity.UserManager<Domain.Entities.ApplicationUser> _userManager;

        public ReviewService(
            IUnitOfWork unitOfWork,
            Microsoft.AspNetCore.Identity.UserManager<Domain.Entities.ApplicationUser> userManager)
        {
            _unitOfWork = unitOfWork;
            _userManager = userManager;
        }

        public async Task<Result<PagedResult<ReviewDto>>> GetForProductAsync(Guid productId, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        {
            pageSize = Math.Clamp(pageSize, 1, 100);
            pageNumber = Math.Max(pageNumber, 1);

            var query = _unitOfWork.Repository<Review>().Query()
                .Where(r => r.ProductId == productId)
                .OrderByDescending(r => r.CreatedAtUtc);

            var totalCount = await query.CountAsync(cancellationToken);
            var pageReviews = await query.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

            // Replaces `.Include(r => r.User).Include(r => r.Images)`: batch-resolve the reviewers
            // (from Identity's Mongo-backed Users collection) and review images in two queries.
            var userIds = pageReviews.Select(r => r.UserId).Distinct().ToList();
            var users = (await _userManager.Users.Where(u => userIds.Contains(u.Id)).ToListAsync(cancellationToken))
                .ToDictionary(u => u.Id);
            var reviewIds = pageReviews.Select(r => r.Id).ToList();
            var imagesByReview = (await _unitOfWork.Repository<ReviewImage>()
                    .FindAsync(i => reviewIds.Contains(i.ReviewId), cancellationToken))
                .GroupBy(i => i.ReviewId).ToDictionary(g => g.Key, g => g.Select(i => i.Url).ToList());

            var items = pageReviews.Select(r =>
            {
                users.TryGetValue(r.UserId, out var user);
                imagesByReview.TryGetValue(r.Id, out var imageUrls);
                return new ReviewDto(
                    r.Id, r.UserId, user?.FullName ?? string.Empty, user?.AvatarUrl, r.Rating, r.Title, r.Comment,
                    r.IsVerifiedPurchase, r.HelpfulCount, imageUrls ?? new List<string>(), r.CreatedAtUtc);
            }).ToList();

            return Result.Success(new PagedResult<ReviewDto> { Items = items, PageNumber = pageNumber, PageSize = pageSize, TotalCount = totalCount });
        }

        public async Task<Result<ReviewDto>> CreateAsync(Guid userId, Guid productId, CreateReviewRequest request, CancellationToken cancellationToken = default)
        {
            var productExists = await _unitOfWork.Repository<Product>().ExistsAsync(p => p.Id == productId, cancellationToken);
            if (!productExists)
            {
                return Result.NotFound<ReviewDto>("Product", productId);
            }

            var alreadyReviewed = await _unitOfWork.Repository<Review>()
                .ExistsAsync(r => r.ProductId == productId && r.UserId == userId, cancellationToken);
            if (alreadyReviewed)
            {
                return Result.Failure<ReviewDto>("You've already reviewed this product.", "ALREADY_REVIEWED");
            }

            // `oi.Order.UserId == userId && oi.Order.Status == Delivered` used to be an EF join
            // across OrderItem->Order. Resolve the user's delivered order ids first, then check
            // whether any OrderItem for this product belongs to one of them.
            var deliveredOrderIds = (await _unitOfWork.Repository<Order>()
                    .FindAsync(o => o.UserId == userId && o.Status == OrderStatus.Delivered, cancellationToken))
                .Select(o => o.Id).ToList();
            var isVerifiedPurchase = deliveredOrderIds.Count > 0 && await _unitOfWork.Repository<OrderItem>()
                .ExistsAsync(oi => oi.ProductId == productId && deliveredOrderIds.Contains(oi.OrderId), cancellationToken);

            var review = new Review
            {
                ProductId = productId,
                UserId = userId,
                Rating = request.Rating,
                Title = request.Title,
                Comment = request.Comment,
                IsVerifiedPurchase = isVerifiedPurchase
            };

            await _unitOfWork.Repository<Review>().AddAsync(review, cancellationToken);
            await RecomputeProductRatingAsync(productId, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var user = await _userManager.FindByIdAsync(userId.ToString());

            return Result.Success(new ReviewDto(
                review.Id, userId, user?.FullName ?? string.Empty, user?.AvatarUrl,
                review.Rating, review.Title, review.Comment, review.IsVerifiedPurchase, 0,
                Array.Empty<string>(), review.CreatedAtUtc));
        }

        public async Task<Result> DeleteAsync(Guid userId, Guid reviewId, bool isAdmin, CancellationToken cancellationToken = default)
        {
            var review = await _unitOfWork.Repository<Review>().GetByIdAsync(reviewId, cancellationToken);
            if (review is null)
            {
                return Result.NotFound("Review", reviewId);
            }

            if (!isAdmin && review.UserId != userId)
            {
                return Result.Failure("You can only delete your own reviews.", "FORBIDDEN");
            }

            var productId = review.ProductId;
            _unitOfWork.Repository<Review>().SoftDelete(review);
            await RecomputeProductRatingAsync(productId, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }

        public async Task<Result> MarkHelpfulAsync(Guid reviewId, CancellationToken cancellationToken = default)
        {
            var review = await _unitOfWork.Repository<Review>().GetByIdAsync(reviewId, cancellationToken);
            if (review is null)
            {
                return Result.NotFound("Review", reviewId);
            }

            review.HelpfulCount++;
            _unitOfWork.Repository<Review>().Update(review);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }

        private async Task RecomputeProductRatingAsync(Guid productId, CancellationToken cancellationToken)
        {
            var product = await _unitOfWork.Repository<Product>().GetByIdAsync(productId, cancellationToken);
            if (product is null) return;

            var reviews = await _unitOfWork.Repository<Review>().Query()
                .Where(r => r.ProductId == productId).ToListAsync(cancellationToken);

            product.ReviewCount = reviews.Count;
            product.AverageRating = reviews.Count == 0 ? 0 : Math.Round(reviews.Average(r => r.Rating), 1);
            _unitOfWork.Repository<Product>().Update(product);
        }
    }
}
