namespace ECommerce.Application.Cart.DTOs;

public record CartItemDto(
    Guid Id, Guid ProductId, string ProductName, string Slug, string? ImageUrl,
    Guid? ProductVariantId, string? VariantDescription,
    decimal UnitPrice, int Quantity, decimal LineTotal, int AvailableStock);

public record CartDto(IReadOnlyList<CartItemDto> Items, decimal Subtotal, int TotalItems);

public record AddToCartRequest(Guid ProductId, Guid? ProductVariantId, int Quantity);
public record UpdateCartItemRequest(int Quantity);
