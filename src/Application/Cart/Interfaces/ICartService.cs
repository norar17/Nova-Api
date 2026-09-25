using ECommerce.Application.Cart.DTOs;
using ECommerce.Shared;

namespace ECommerce.Application.Cart.Interfaces;

public interface ICartService
{
    Task<Result<CartDto>> GetCartAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Result<CartDto>> AddItemAsync(Guid userId, AddToCartRequest request, CancellationToken cancellationToken = default);
    Task<Result<CartDto>> UpdateItemAsync(Guid userId, Guid cartItemId, UpdateCartItemRequest request, CancellationToken cancellationToken = default);
    Task<Result<CartDto>> RemoveItemAsync(Guid userId, Guid cartItemId, CancellationToken cancellationToken = default);
    Task<Result> ClearCartAsync(Guid userId, CancellationToken cancellationToken = default);
}
