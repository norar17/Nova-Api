using ECommerce.Domain.Enums;

namespace ECommerce.Application.Orders.DTOs;

public record CheckoutRequest(
    string ShippingFullName,
    string ShippingPhoneNumber,
    string ShippingLine1,
    string? ShippingLine2,
    string ShippingCity,
    string ShippingState,
    string ShippingPostalCode,
    string ShippingCountry,
    string? CouponCode);

public record CheckoutResponse(Guid OrderId, string OrderNumber, decimal Total, string ClientSecret, string PaymentIntentId);

public record OrderItemDto(
    Guid Id, Guid ProductId, string ProductName, string? ProductImageUrl,
    string? VariantDescription, decimal UnitPrice, int Quantity, decimal LineTotal);

public record OrderListItemDto(Guid Id, string OrderNumber, OrderStatus Status, decimal Total, int ItemCount, string? ThumbnailUrl, DateTime CreatedAtUtc);

public record OrderDetailDto(
    Guid Id, string OrderNumber, OrderStatus Status,
    decimal Subtotal, decimal ShippingCost, decimal TaxAmount, decimal DiscountAmount, decimal Total,
    string ShippingFullName, string ShippingLine1, string? ShippingLine2, string ShippingCity,
    string ShippingState, string ShippingPostalCode, string ShippingCountry,
    string? TrackingNumber, string? Carrier,
    DateTime? ShippedAtUtc, DateTime? DeliveredAtUtc,
    IReadOnlyList<OrderItemDto> Items,
    DateTime CreatedAtUtc);

public record UpdateOrderStatusRequest(OrderStatus Status, string? TrackingNumber, string? Carrier);
public record CancelOrderRequest(string Reason);
