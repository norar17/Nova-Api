using ECommerce.Application.Orders.DTOs;
using ECommerce.Shared;

namespace ECommerce.Application.Orders.Interfaces;

public interface ICheckoutService
{
    Task<Result<CheckoutResponse>> CheckoutAsync(Guid userId, CheckoutRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invoked by the Stripe webhook endpoint after signature verification. Marks the payment/order
    /// as paid, decrements stock, clears the user's cart, and sends the confirmation email —
    /// all in one place so this can never happen twice or be skipped.
    /// </summary>
    Task<Result> HandlePaymentSucceededAsync(string paymentIntentId, CancellationToken cancellationToken = default);
    Task<Result> HandlePaymentFailedAsync(string paymentIntentId, string reason, CancellationToken cancellationToken = default);
}

public interface IOrderService
{
    Task<Result<PagedResult<OrderListItemDto>>> GetMyOrdersAsync(Guid userId, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task<Result<OrderDetailDto>> GetByIdAsync(Guid userId, Guid orderId, bool isAdmin, CancellationToken cancellationToken = default);
    Task<Result> CancelOrderAsync(Guid userId, Guid orderId, CancelOrderRequest request, CancellationToken cancellationToken = default);

    // Seller
    Task<Result<PagedResult<OrderListItemDto>>> GetByStoreAsync(Guid storeId, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task<Result<bool>> StoreHasAccessToOrderAsync(Guid storeId, Guid orderId, CancellationToken cancellationToken = default);

    // Admin
    Task<Result<PagedResult<OrderListItemDto>>> GetAllAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task<Result<OrderDetailDto>> UpdateStatusAsync(Guid orderId, UpdateOrderStatusRequest request, CancellationToken cancellationToken = default);
}
