using MongoDB.Driver.Linq;
using ECommerce.Application.Orders.DTOs;
using ECommerce.Application.Orders.Interfaces;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Enums;
using ECommerce.Domain.Interfaces;
using ECommerce.Shared;

namespace ECommerce.Application.Orders.Services;

public class OrderService : IOrderService
{
    private readonly IUnitOfWork _unitOfWork;

    public OrderService(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<PagedResult<OrderListItemDto>>> GetMyOrdersAsync(Guid userId, int pageNumber, int pageSize, CancellationToken cancellationToken = default) =>
        await GetPagedAsync(_unitOfWork.Repository<Order>().Query().Where(o => o.UserId == userId), pageNumber, pageSize, cancellationToken);

    public async Task<Result<PagedResult<OrderListItemDto>>> GetByStoreAsync(Guid storeId, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        // `o.Items.Any(i => i.Product.StoreId == storeId)` used to be a two-level EF join. Resolve it
        // by hand: this store's product ids -> order items referencing them -> the distinct order ids.
        var orderIds = await OrderIdsForStoreAsync(storeId, cancellationToken);
        return await GetPagedAsync(_unitOfWork.Repository<Order>().Query().Where(o => orderIds.Contains(o.Id)), pageNumber, pageSize, cancellationToken);
    }

    public async Task<Result<bool>> StoreHasAccessToOrderAsync(Guid storeId, Guid orderId, CancellationToken cancellationToken = default)
    {
        var orderIds = await OrderIdsForStoreAsync(storeId, cancellationToken);
        return Result.Success(orderIds.Contains(orderId));
    }

    private async Task<List<Guid>> OrderIdsForStoreAsync(Guid storeId, CancellationToken cancellationToken)
    {
        var productIds = (await _unitOfWork.Repository<Product>().FindAsync(p => p.StoreId == storeId, cancellationToken))
            .Select(p => p.Id).ToList();
        if (productIds.Count == 0) return new List<Guid>();

        return (await _unitOfWork.Repository<OrderItem>().FindAsync(i => productIds.Contains(i.ProductId), cancellationToken))
            .Select(i => i.OrderId).Distinct().ToList();
    }

    public async Task<Result<PagedResult<OrderListItemDto>>> GetAllAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default) =>
        await GetPagedAsync(_unitOfWork.Repository<Order>().Query(), pageNumber, pageSize, cancellationToken);

    private async Task<Result<PagedResult<OrderListItemDto>>> GetPagedAsync(
        IQueryable<Order> query, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        pageNumber = Math.Max(pageNumber, 1);

        var totalCount = await query.CountAsync(cancellationToken);

        var orders = await query
            .OrderByDescending(o => o.CreatedAtUtc)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        // Replaces `o.Items` (used to be an EF navigation projected inline): batch-fetch every
        // OrderItem for this page of orders in one query and group them back by order.
        var orderIds = orders.Select(o => o.Id).ToList();
        var itemsByOrder = orderIds.Count == 0
            ? new Dictionary<Guid, List<OrderItem>>()
            : (await _unitOfWork.Repository<OrderItem>().FindAsync(i => orderIds.Contains(i.OrderId), cancellationToken))
                .GroupBy(i => i.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        var items = orders.Select(o =>
        {
            itemsByOrder.TryGetValue(o.Id, out var orderItems);
            orderItems ??= new List<OrderItem>();
            var firstImage = orderItems.OrderBy(i => i.CreatedAtUtc).Select(i => i.ProductImageUrl).FirstOrDefault();
            return new OrderListItemDto(o.Id, o.OrderNumber, o.Status, o.Total, orderItems.Count, firstImage, o.CreatedAtUtc);
        }).ToList();

        return Result.Success(new PagedResult<OrderListItemDto>
        {
            Items = items,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalCount = totalCount
        });
    }

    public async Task<Result<OrderDetailDto>> GetByIdAsync(Guid userId, Guid orderId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var order = await _unitOfWork.Repository<Order>().GetByIdAsync(orderId, cancellationToken);

        if (order is null || (!isAdmin && order.UserId != userId))
        {
            return Result.NotFound<OrderDetailDto>("Order", orderId);
        }

        var items = await _unitOfWork.Repository<OrderItem>().FindAsync(i => i.OrderId == orderId, cancellationToken);
        return Result.Success(ToDetailDto(order, items));
    }

    public async Task<Result> CancelOrderAsync(Guid userId, Guid orderId, CancelOrderRequest request, CancellationToken cancellationToken = default)
    {
        var order = await _unitOfWork.Repository<Order>().FindOneAsync(o => o.Id == orderId && o.UserId == userId, cancellationToken);

        if (order is null)
        {
            return Result.NotFound("Order", orderId);
        }

        if (order.Status is OrderStatus.Shipped or OrderStatus.OutForDelivery or OrderStatus.Delivered)
        {
            return Result.Failure("This order can no longer be cancelled — it has already shipped.", "CANNOT_CANCEL");
        }

        if (order.Status is OrderStatus.Cancelled or OrderStatus.Refunded)
        {
            return Result.Failure("This order is already cancelled.", "ALREADY_CANCELLED");
        }

        var wasPaid = order.Status == OrderStatus.Paid;

        order.Status = OrderStatus.Cancelled;
        order.CancelledAtUtc = DateTime.UtcNow;
        order.CancellationReason = request.Reason;
        _unitOfWork.Repository<Order>().Update(order);

        if (wasPaid)
        {
            // Restock items — payment was already captured, so this only releases inventory.
            // The actual refund is issued separately by an admin via the Stripe dashboard/refund endpoint.
            var items = await _unitOfWork.Repository<OrderItem>().FindAsync(i => i.OrderId == orderId, cancellationToken);
            foreach (var item in items)
            {
                if (item.ProductVariantId.HasValue)
                {
                    var variant = await _unitOfWork.Repository<ProductVariant>().GetByIdAsync(item.ProductVariantId.Value, cancellationToken);
                    if (variant is not null)
                    {
                        variant.StockQuantity += item.Quantity;
                        _unitOfWork.Repository<ProductVariant>().Update(variant);
                    }
                }

                var product = await _unitOfWork.Repository<Product>().GetByIdAsync(item.ProductId, cancellationToken);
                if (product is not null)
                {
                    product.StockQuantity += item.Quantity;
                    _unitOfWork.Repository<Product>().Update(product);
                }
            }
        }

        return Result.Success();
    }

    public async Task<Result<OrderDetailDto>> UpdateStatusAsync(Guid orderId, UpdateOrderStatusRequest request, CancellationToken cancellationToken = default)
    {
        var order = await _unitOfWork.Repository<Order>().GetByIdAsync(orderId, cancellationToken);

        if (order is null)
        {
            return Result.NotFound<OrderDetailDto>("Order", orderId);
        }

        order.Status = request.Status;
        if (request.TrackingNumber is not null) order.TrackingNumber = request.TrackingNumber;
        if (request.Carrier is not null) order.Carrier = request.Carrier;

        if (request.Status == OrderStatus.Shipped && order.ShippedAtUtc is null)
        {
            order.ShippedAtUtc = DateTime.UtcNow;
        }
        if (request.Status == OrderStatus.Delivered && order.DeliveredAtUtc is null)
        {
            order.DeliveredAtUtc = DateTime.UtcNow;
        }

        _unitOfWork.Repository<Order>().Update(order);

        var items = await _unitOfWork.Repository<OrderItem>().FindAsync(i => i.OrderId == orderId, cancellationToken);
        return Result.Success(ToDetailDto(order, items));
    }

    private static OrderDetailDto ToDetailDto(Order o, IReadOnlyList<OrderItem> items) => new(
        o.Id, o.OrderNumber, o.Status, o.Subtotal, o.ShippingCost, o.TaxAmount, o.DiscountAmount, o.Total,
        o.ShippingFullName, o.ShippingLine1, o.ShippingLine2, o.ShippingCity, o.ShippingState, o.ShippingPostalCode, o.ShippingCountry,
        o.TrackingNumber, o.Carrier, o.ShippedAtUtc, o.DeliveredAtUtc,
        items.Select(i => new OrderItemDto(i.Id, i.ProductId, i.ProductName, i.ProductImageUrl, i.VariantDescription, i.UnitPrice, i.Quantity, i.LineTotal)).ToList(),
        o.CreatedAtUtc);
}
