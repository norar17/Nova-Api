using ECommerce.Application.Common.Interfaces;
using MongoDB.Driver.Linq;
using ECommerce.Application.Orders.DTOs;
using ECommerce.Application.Orders.Interfaces;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Enums;
using ECommerce.Domain.Interfaces;
using ECommerce.Shared;
using Microsoft.AspNetCore.Identity;

namespace ECommerce.Application.Orders.Services;

/// <summary>
/// IMPORTANT: see the class-level remarks on Infrastructure/Persistence/Repository.cs. Everything in
/// here used to run inside one EF `SaveChangesAsync` transaction; on a standalone (non-replica-set)
/// MongoDB instance there is currently no equivalent — each repository call commits independently.
/// If you deploy this against real payments, either point Mongo at a replica set and wrap the
/// multi-write sections below in a client session transaction, or accept (and monitor for) the small
/// window where an Order/Payment/stock update could partially fail.
/// </summary>
public class CheckoutService : ICheckoutService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPaymentService _paymentService;
    private readonly IEmailService _emailService;
    private readonly UserManager<ApplicationUser> _userManager;

    private const decimal FlatShippingCost = 5.99m;
    private const decimal FreeShippingThreshold = 75.00m;
    private const decimal TaxRate = 0.08m;

    public CheckoutService(
        IUnitOfWork unitOfWork, IPaymentService paymentService, IEmailService emailService,
        UserManager<ApplicationUser> userManager)
    {
        _unitOfWork = unitOfWork;
        _paymentService = paymentService;
        _emailService = emailService;
        _userManager = userManager;
    }

    public async Task<Result<CheckoutResponse>> CheckoutAsync(Guid userId, CheckoutRequest request, CancellationToken cancellationToken = default)
    {
        var cartItems = await _unitOfWork.Repository<CartItem>().FindAsync(c => c.UserId == userId, cancellationToken);
        if (cartItems.Count == 0)
        {
            return Result.Failure<CheckoutResponse>("Your cart is empty.", "EMPTY_CART");
        }

        // Replaces `.Include(c => c.Product).ThenInclude(p => p.Images).Include(c => c.ProductVariant)`
        var productIds = cartItems.Select(c => c.ProductId).Distinct().ToList();
        var variantIds = cartItems.Where(c => c.ProductVariantId.HasValue).Select(c => c.ProductVariantId!.Value).Distinct().ToList();
        var products = (await _unitOfWork.Repository<Product>().GetByIdsAsync(productIds, cancellationToken)).ToDictionary(p => p.Id);
        var variants = (await _unitOfWork.Repository<ProductVariant>().GetByIdsAsync(variantIds, cancellationToken)).ToDictionary(v => v.Id);
        var thumbnailByProduct = (await _unitOfWork.Repository<ProductImage>().FindAsync(i => productIds.Contains(i.ProductId) && i.IsThumbnail, cancellationToken))
            .GroupBy(i => i.ProductId).ToDictionary(g => g.Key, g => g.First().Url);

        foreach (var item in cartItems)
        {
            if (!products.TryGetValue(item.ProductId, out var p))
            {
                return Result.Failure<CheckoutResponse>("One of the items in your cart no longer exists.", "PRODUCT_NOT_FOUND");
            }
            var variant = item.ProductVariantId.HasValue && variants.TryGetValue(item.ProductVariantId.Value, out var v) ? v : null;
            var available = variant?.StockQuantity ?? p.StockQuantity;
            if (item.Quantity > available)
            {
                return Result.Failure<CheckoutResponse>($"'{p.Name}' no longer has enough stock.", "INSUFFICIENT_STOCK");
            }
        }

        var subtotal = cartItems.Sum(c =>
        {
            var p = products[c.ProductId];
            var variant = c.ProductVariantId.HasValue && variants.TryGetValue(c.ProductVariantId.Value, out var v) ? v : null;
            return ((p.DiscountPrice ?? p.Price) + (variant?.PriceAdjustment ?? 0)) * c.Quantity;
        });

        Coupon? coupon = null;
        decimal discountAmount = 0;

        if (!string.IsNullOrWhiteSpace(request.CouponCode))
        {
            var couponResult = await ValidateAndComputeCouponAsync(request.CouponCode, userId, subtotal, cancellationToken);
            if (couponResult.IsFailure)
            {
                return Result.Failure<CheckoutResponse>(couponResult.Error!, couponResult.ErrorCode ?? "COUPON_ERROR");
            }

            (coupon, discountAmount) = couponResult.Value;
        }

        var shippingCost = subtotal - discountAmount >= FreeShippingThreshold ? 0 : FlatShippingCost;
        var taxAmount = Math.Round((subtotal - discountAmount) * TaxRate, 2);
        var total = subtotal - discountAmount + shippingCost + taxAmount;

        var order = new Order
        {
            OrderNumber = GenerateOrderNumber(),
            UserId = userId,
            Status = OrderStatus.Pending,
            Subtotal = subtotal,
            ShippingCost = shippingCost,
            TaxAmount = taxAmount,
            DiscountAmount = discountAmount,
            Total = total,
            CouponId = coupon?.Id,
            ShippingFullName = request.ShippingFullName,
            ShippingPhoneNumber = request.ShippingPhoneNumber,
            ShippingLine1 = request.ShippingLine1,
            ShippingLine2 = request.ShippingLine2,
            ShippingCity = request.ShippingCity,
            ShippingState = request.ShippingState,
            ShippingPostalCode = request.ShippingPostalCode,
            ShippingCountry = request.ShippingCountry
        };

        await _unitOfWork.Repository<Order>().AddAsync(order, cancellationToken);

        // Order.Items is not persisted as an embedded array (see BsonMappings) — OrderItem is its
        // own collection, linked by OrderId, so each one is inserted directly rather than assigned
        // to order.Items and relying on EF's cascade insert.
        foreach (var c in cartItems)
        {
            var p = products[c.ProductId];
            var variant = c.ProductVariantId.HasValue && variants.TryGetValue(c.ProductVariantId.Value, out var v) ? v : null;
            var unitPrice = (p.DiscountPrice ?? p.Price) + (variant?.PriceAdjustment ?? 0);
            var variantDescription = variant is null ? null :
                string.Join(", ", new[] { variant.Size, variant.Color }.Where(s => s is not null));
            thumbnailByProduct.TryGetValue(p.Id, out var thumbnailUrl);

            await _unitOfWork.Repository<OrderItem>().AddAsync(new OrderItem
            {
                OrderId = order.Id,
                ProductId = c.ProductId,
                ProductVariantId = c.ProductVariantId,
                ProductName = p.Name,
                ProductImageUrl = thumbnailUrl,
                VariantDescription = variantDescription,
                UnitPrice = unitPrice,
                Quantity = c.Quantity,
                LineTotal = unitPrice * c.Quantity
            }, cancellationToken);
        }

        var (clientSecret, paymentIntentId) = await _paymentService.CreatePaymentIntentAsync(total, "usd", order.Id, cancellationToken);

        await _unitOfWork.Repository<Payment>().AddAsync(new Payment
        {
            OrderId = order.Id,
            Provider = PaymentProvider.Stripe,
            Status = PaymentStatus.Pending,
            StripePaymentIntentId = paymentIntentId,
            Amount = total,
            Currency = "usd"
        }, cancellationToken);

        // Clear the cart now — the order already snapshots everything (name, price, quantity) at
        // this point, so the cart's job is done. Waiting for the webhook to clear it would leave a
        // stale cart sitting there for however long payment confirmation takes (or forever, if a
        // webhook never arrives in a broken local dev setup) even though the order was placed.
        foreach (var item in cartItems)
        {
            _unitOfWork.Repository<CartItem>().Remove(item);
        }

        return Result.Success(new CheckoutResponse(order.Id, order.OrderNumber, total, clientSecret, paymentIntentId));
    }

    public async Task<Result> HandlePaymentSucceededAsync(string paymentIntentId, CancellationToken cancellationToken = default)
    {
        var payment = await _unitOfWork.Repository<Payment>().FindOneAsync(p => p.StripePaymentIntentId == paymentIntentId, cancellationToken);
        if (payment is null)
        {
            return Result.NotFound("Payment", paymentIntentId);
        }

        if (payment.Status == PaymentStatus.Succeeded)
        {
            return Result.Success(); // webhook delivered twice — no-op, keeps this idempotent
        }

        payment.Status = PaymentStatus.Succeeded;
        payment.PaidAtUtc = DateTime.UtcNow;
        _unitOfWork.Repository<Payment>().Update(payment);

        // Replaces `.Include(p => p.Order).ThenInclude(o => o.Items/.User/.Coupon)`.
        var order = await _unitOfWork.Repository<Order>().GetByIdAsync(payment.OrderId, cancellationToken);
        if (order is null)
        {
            return Result.NotFound("Order", payment.OrderId);
        }

        order.Status = OrderStatus.Paid;
        _unitOfWork.Repository<Order>().Update(order);

        var items = await _unitOfWork.Repository<OrderItem>().FindAsync(i => i.OrderId == order.Id, cancellationToken);
        foreach (var item in items)
        {
            if (item.ProductVariantId.HasValue)
            {
                var variant = await _unitOfWork.Repository<ProductVariant>().GetByIdAsync(item.ProductVariantId.Value, cancellationToken);
                if (variant is not null)
                {
                    variant.StockQuantity = Math.Max(0, variant.StockQuantity - item.Quantity);
                    _unitOfWork.Repository<ProductVariant>().Update(variant);
                }
            }

            var product = await _unitOfWork.Repository<Product>().GetByIdAsync(item.ProductId, cancellationToken);
            if (product is not null)
            {
                product.StockQuantity = Math.Max(0, product.StockQuantity - item.Quantity);
                product.SalesCount += item.Quantity;
                _unitOfWork.Repository<Product>().Update(product);
            }
        }

        if (order.CouponId.HasValue)
        {
            var coupon = await _unitOfWork.Repository<Coupon>().GetByIdAsync(order.CouponId.Value, cancellationToken);
            if (coupon is not null)
            {
                coupon.UsageCount++;
                _unitOfWork.Repository<Coupon>().Update(coupon);
            }
        }

        // Safety net only — CheckoutAsync already clears the cart eagerly when the order is placed,
        // so this normally finds nothing left to remove. Kept in case an order was ever created
        // through some other path that skipped that step.
        var cartItems = await _unitOfWork.Repository<CartItem>().FindAsync(c => c.UserId == order.UserId, cancellationToken);
        foreach (var item in cartItems)
        {
            _unitOfWork.Repository<CartItem>().Remove(item);
        }

        var user = await _userManager.FindByIdAsync(order.UserId.ToString());
        if (user?.Email is not null)
        {
            await _emailService.SendPaymentConfirmationAsync(user.Email, order.OrderNumber, payment.Amount, cancellationToken);
            await _emailService.SendOrderConfirmationAsync(user.Email, order.OrderNumber, order.Total, cancellationToken);
        }

        return Result.Success();
    }

    public async Task<Result> HandlePaymentFailedAsync(string paymentIntentId, string reason, CancellationToken cancellationToken = default)
    {
        var payment = await _unitOfWork.Repository<Payment>().FindOneAsync(p => p.StripePaymentIntentId == paymentIntentId, cancellationToken);
        if (payment is null)
        {
            return Result.NotFound("Payment", paymentIntentId);
        }

        payment.Status = PaymentStatus.Failed;
        payment.FailureReason = reason;
        _unitOfWork.Repository<Payment>().Update(payment);

        var order = await _unitOfWork.Repository<Order>().GetByIdAsync(payment.OrderId, cancellationToken);
        if (order is not null)
        {
            order.Status = OrderStatus.Failed;
            _unitOfWork.Repository<Order>().Update(order);
        }

        return Result.Success();
    }

    private async Task<Result<(Coupon Coupon, decimal DiscountAmount)>> ValidateAndComputeCouponAsync(
        string code, Guid userId, decimal subtotal, CancellationToken cancellationToken)
    {
        var coupon = await _unitOfWork.Repository<Coupon>().FindOneAsync(c => c.Code == code.ToUpper(), cancellationToken);

        if (coupon is null || coupon.Status != CouponStatus.Active)
        {
            return Result.Failure<(Coupon, decimal)>("Invalid or inactive coupon code.", "INVALID_COUPON");
        }

        var now = DateTime.UtcNow;
        if (now < coupon.StartsAtUtc || now > coupon.ExpiresAtUtc)
        {
            return Result.Failure<(Coupon, decimal)>("This coupon has expired.", "COUPON_EXPIRED");
        }

        if (coupon.UsageLimit.HasValue && coupon.UsageCount >= coupon.UsageLimit)
        {
            return Result.Failure<(Coupon, decimal)>("This coupon has reached its usage limit.", "COUPON_LIMIT_REACHED");
        }

        if (coupon.PerUserLimit.HasValue)
        {
            var userUsageCount = await _unitOfWork.Repository<Order>()
                .Query().CountAsync(o => o.CouponId == coupon.Id && o.UserId == userId, cancellationToken);
            if (userUsageCount >= coupon.PerUserLimit)
            {
                return Result.Failure<(Coupon, decimal)>("You've already used this coupon the maximum number of times.", "COUPON_LIMIT_REACHED");
            }
        }

        if (coupon.MinimumOrderAmount.HasValue && subtotal < coupon.MinimumOrderAmount)
        {
            return Result.Failure<(Coupon, decimal)>(
                $"This coupon requires a minimum order of ${coupon.MinimumOrderAmount:0.00}.", "COUPON_MINIMUM_NOT_MET");
        }

        var discount = coupon.Type == DiscountType.Percentage
            ? subtotal * (coupon.Value / 100m)
            : coupon.Value;

        if (coupon.MaximumDiscountAmount.HasValue)
        {
            discount = Math.Min(discount, coupon.MaximumDiscountAmount.Value);
        }

        discount = Math.Min(discount, subtotal); // never discount below zero
        return Result.Success((coupon, Math.Round(discount, 2)));
    }

    private static string GenerateOrderNumber() =>
        $"ORD-{DateTime.UtcNow:yyyy}-{Random.Shared.Next(100000, 999999)}";
}
