using ECommerce.Domain.Entities;
using ECommerce.Domain.Interfaces;
using ECommerce.Shared;

namespace ECommerce.Application.Wishlist.DTOs
{
    public record WishlistItemDto(Guid Id, Guid ProductId, string ProductName, string Slug, string? ImageUrl,
        decimal Price, decimal? DiscountPrice, bool InStock);
}

namespace ECommerce.Application.Wishlist.Interfaces
{
    using ECommerce.Application.Wishlist.DTOs;

    public interface IWishlistService
    {
        Task<Result<IReadOnlyList<WishlistItemDto>>> GetAsync(Guid userId, CancellationToken cancellationToken = default);
        Task<Result> AddAsync(Guid userId, Guid productId, CancellationToken cancellationToken = default);
        Task<Result> RemoveAsync(Guid userId, Guid productId, CancellationToken cancellationToken = default);
    }
}

namespace ECommerce.Application.Wishlist.Services
{
    using ECommerce.Application.Wishlist.DTOs;
    using ECommerce.Application.Wishlist.Interfaces;

    public class WishlistService : IWishlistService
    {
        private readonly IUnitOfWork _unitOfWork;

        public WishlistService(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

        public async Task<Result<IReadOnlyList<WishlistItemDto>>> GetAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var wishlistItems = await _unitOfWork.Repository<WishlistItem>().FindAsync(w => w.UserId == userId, cancellationToken);
            if (wishlistItems.Count == 0)
            {
                return Result.Success<IReadOnlyList<WishlistItemDto>>(new List<WishlistItemDto>());
            }

            var productIds = wishlistItems.Select(w => w.ProductId).Distinct().ToList();
            var products = (await _unitOfWork.Repository<Product>().GetByIdsAsync(productIds, cancellationToken)).ToDictionary(p => p.Id);
            var imagesByProduct = (await _unitOfWork.Repository<ProductImage>().FindAsync(i => productIds.Contains(i.ProductId), cancellationToken))
                .GroupBy(i => i.ProductId).ToDictionary(g => g.Key, g => g.ToList());

            var items = wishlistItems
                .Where(w => products.ContainsKey(w.ProductId)) // skip orphaned references defensively
                .Select(w =>
                {
                    var product = products[w.ProductId];
                    imagesByProduct.TryGetValue(product.Id, out var images);
                    var imageUrl = images?.FirstOrDefault(i => i.IsThumbnail)?.Url ?? images?.FirstOrDefault()?.Url;

                    return new WishlistItemDto(
                        w.Id, w.ProductId, product.Name, product.Slug, imageUrl,
                        product.Price, product.DiscountPrice, product.StockQuantity > 0);
                })
                .ToList();

            return Result.Success<IReadOnlyList<WishlistItemDto>>(items);
        }

        public async Task<Result> AddAsync(Guid userId, Guid productId, CancellationToken cancellationToken = default)
        {
            var productExists = await _unitOfWork.Repository<Product>().ExistsAsync(p => p.Id == productId, cancellationToken);
            if (!productExists)
            {
                return Result.NotFound("Product", productId);
            }

            var alreadyExists = await _unitOfWork.Repository<WishlistItem>()
                .ExistsAsync(w => w.UserId == userId && w.ProductId == productId, cancellationToken);
            if (alreadyExists)
            {
                return Result.Success(); // idempotent
            }

            await _unitOfWork.Repository<WishlistItem>().AddAsync(new WishlistItem { UserId = userId, ProductId = productId }, cancellationToken);
            return Result.Success();
        }

        public async Task<Result> RemoveAsync(Guid userId, Guid productId, CancellationToken cancellationToken = default)
        {
            var item = await _unitOfWork.Repository<WishlistItem>()
                .FindOneAsync(w => w.UserId == userId && w.ProductId == productId, cancellationToken);

            if (item is null)
            {
                return Result.Success(); // idempotent
            }

            _unitOfWork.Repository<WishlistItem>().Remove(item);
            return Result.Success();
        }
    }
}
