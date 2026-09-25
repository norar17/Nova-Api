using ECommerce.Application.Cart.DTOs;
using ECommerce.Application.Cart.Interfaces;
using MongoDB.Driver.Linq;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Interfaces;
using ECommerce.Shared;

namespace ECommerce.Application.Cart.Services;

public class CartService : ICartService
{
    private readonly IUnitOfWork _unitOfWork;

    public CartService(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<CartDto>> GetCartAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var items = await LoadItemsAsync(userId, cancellationToken);
        return Result.Success(await ToCartDtoAsync(items, cancellationToken));
    }

    public async Task<Result<CartDto>> AddItemAsync(Guid userId, AddToCartRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Quantity <= 0)
        {
            return Result.Failure<CartDto>("Quantity must be at least 1.", "INVALID_QUANTITY");
        }

        var product = await _unitOfWork.Repository<Product>()
            .FindOneAsync(p => p.Id == request.ProductId && p.IsActive, cancellationToken);

        if (product is null)
        {
            return Result.NotFound<CartDto>("Product", request.ProductId);
        }

        var availableStock = product.StockQuantity;
        if (request.ProductVariantId.HasValue)
        {
            var variant = await _unitOfWork.Repository<ProductVariant>()
                .FindOneAsync(v => v.Id == request.ProductVariantId && v.ProductId == product.Id, cancellationToken);
            if (variant is null)
            {
                return Result.Failure<CartDto>("Selected variant does not exist for this product.", "INVALID_VARIANT");
            }
            availableStock = variant.StockQuantity;
        }

        var existing = await _unitOfWork.Repository<CartItem>()
            .FindOneAsync(c => c.UserId == userId && c.ProductId == request.ProductId
                && c.ProductVariantId == request.ProductVariantId, cancellationToken);

        var requestedTotal = (existing?.Quantity ?? 0) + request.Quantity;
        if (requestedTotal > availableStock)
        {
            return Result.Failure<CartDto>("Not enough stock available.", "INSUFFICIENT_STOCK");
        }

        if (existing is not null)
        {
            existing.Quantity = requestedTotal;
            _unitOfWork.Repository<CartItem>().Update(existing);
        }
        else
        {
            await _unitOfWork.Repository<CartItem>().AddAsync(new CartItem
            {
                UserId = userId,
                ProductId = request.ProductId,
                ProductVariantId = request.ProductVariantId,
                Quantity = request.Quantity
            }, cancellationToken);
        }

        return await GetCartAsync(userId, cancellationToken);
    }

    public async Task<Result<CartDto>> UpdateItemAsync(Guid userId, Guid cartItemId, UpdateCartItemRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Quantity <= 0)
        {
            return Result.Failure<CartDto>("Quantity must be at least 1. Use remove to delete the item.", "INVALID_QUANTITY");
        }

        var item = await _unitOfWork.Repository<CartItem>()
            .FindOneAsync(c => c.Id == cartItemId && c.UserId == userId, cancellationToken);

        if (item is null)
        {
            return Result.NotFound<CartDto>("Cart item", cartItemId);
        }

        int availableStock;
        if (item.ProductVariantId.HasValue)
        {
            var variant = await _unitOfWork.Repository<ProductVariant>().GetByIdAsync(item.ProductVariantId.Value, cancellationToken);
            availableStock = variant?.StockQuantity ?? 0;
        }
        else
        {
            var product = await _unitOfWork.Repository<Product>().GetByIdAsync(item.ProductId, cancellationToken);
            availableStock = product?.StockQuantity ?? 0;
        }

        if (request.Quantity > availableStock)
        {
            return Result.Failure<CartDto>("Not enough stock available.", "INSUFFICIENT_STOCK");
        }

        item.Quantity = request.Quantity;
        _unitOfWork.Repository<CartItem>().Update(item);

        return await GetCartAsync(userId, cancellationToken);
    }

    public async Task<Result<CartDto>> RemoveItemAsync(Guid userId, Guid cartItemId, CancellationToken cancellationToken = default)
    {
        var item = await _unitOfWork.Repository<CartItem>()
            .FindOneAsync(c => c.Id == cartItemId && c.UserId == userId, cancellationToken);

        if (item is null)
        {
            return Result.NotFound<CartDto>("Cart item", cartItemId);
        }

        _unitOfWork.Repository<CartItem>().Remove(item);

        return await GetCartAsync(userId, cancellationToken);
    }

    public async Task<Result> ClearCartAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var items = await _unitOfWork.Repository<CartItem>().FindAsync(c => c.UserId == userId, cancellationToken);

        foreach (var item in items)
        {
            _unitOfWork.Repository<CartItem>().Remove(item);
        }

        return Result.Success();
    }

    private async Task<List<CartItem>> LoadItemsAsync(Guid userId, CancellationToken cancellationToken) =>
        (await _unitOfWork.Repository<CartItem>().FindAsync(c => c.UserId == userId, cancellationToken)).ToList();

    /// <summary>
    /// Replaces `.Include(c => c.Product).ThenInclude(p => p.Images).Include(c => c.ProductVariant)`:
    /// batch-resolves the products, their thumbnail images, and any variants for this page of cart
    /// items in three queries total instead of one per item.
    /// </summary>
    private async Task<CartDto> ToCartDtoAsync(List<CartItem> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0) return new CartDto(new List<CartItemDto>(), 0, 0);

        var productIds = items.Select(i => i.ProductId).Distinct().ToList();
        var variantIds = items.Where(i => i.ProductVariantId.HasValue).Select(i => i.ProductVariantId!.Value).Distinct().ToList();

        var products = (await _unitOfWork.Repository<Product>().GetByIdsAsync(productIds, cancellationToken)).ToDictionary(p => p.Id);
        var variants = (await _unitOfWork.Repository<ProductVariant>().GetByIdsAsync(variantIds, cancellationToken)).ToDictionary(v => v.Id);
        var imagesByProduct = (await _unitOfWork.Repository<ProductImage>().FindAsync(img => productIds.Contains(img.ProductId), cancellationToken))
            .GroupBy(img => img.ProductId).ToDictionary(g => g.Key, g => g.ToList());

        var dtos = new List<CartItemDto>();
        foreach (var c in items)
        {
            if (!products.TryGetValue(c.ProductId, out var product)) continue; // orphaned reference; skip defensively
            ProductVariant? variant = c.ProductVariantId.HasValue && variants.TryGetValue(c.ProductVariantId.Value, out var v) ? v : null;

            var unitPrice = (product.DiscountPrice ?? product.Price) + (variant?.PriceAdjustment ?? 0);
            var availableStock = variant?.StockQuantity ?? product.StockQuantity;

            var variantParts = new List<string>();
            if (variant?.Size is not null) variantParts.Add($"Size: {variant.Size}");
            if (variant?.Color is not null) variantParts.Add($"Color: {variant.Color}");
            var variantDescription = variantParts.Count > 0 ? string.Join(", ", variantParts) : null;

            imagesByProduct.TryGetValue(product.Id, out var images);
            var thumbnailUrl = images?.FirstOrDefault(i => i.IsThumbnail)?.Url ?? images?.FirstOrDefault()?.Url;

            dtos.Add(new CartItemDto(
                c.Id, c.ProductId, product.Name, product.Slug, thumbnailUrl,
                c.ProductVariantId, variantDescription,
                unitPrice, c.Quantity, unitPrice * c.Quantity, availableStock));
        }

        return new CartDto(dtos, dtos.Sum(d => d.LineTotal), dtos.Sum(d => d.Quantity));
    }
}
