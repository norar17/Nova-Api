using System.Security.Claims;
using ECommerce.Application.Common.Interfaces;
using ECommerce.Application.Orders.DTOs;
using ECommerce.Application.Orders.Interfaces;
using ECommerce.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Stripe;

namespace ECommerce.API.Controllers;

[ApiController]
[Route("api/v1/checkout")]
[Authorize]
public class CheckoutController : ControllerBase
{
    private readonly ICheckoutService _checkoutService;
    public CheckoutController(ICheckoutService checkoutService) => _checkoutService = checkoutService;

    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost]
    public async Task<IActionResult> Checkout([FromBody] CheckoutRequest request, CancellationToken cancellationToken)
    {
        var result = await _checkoutService.CheckoutAsync(UserId, request, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<CheckoutResponse>.Ok(result.Value))
            : BadRequest(ApiResponse<CheckoutResponse>.Fail(result.Error!, result.ErrorCode));
    }
}

[ApiController]
[Route("api/v1/orders")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;
    public OrdersController(IOrderService orderService) => _orderService = orderService;

    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private bool IsAdmin => User.IsInRole("Admin");

    [HttpGet]
    public async Task<IActionResult> GetMyOrders([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var result = await _orderService.GetMyOrdersAsync(UserId, pageNumber, pageSize, cancellationToken);
        return Ok(ApiResponse<PagedResult<OrderListItemDto>>.Ok(result.Value));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _orderService.GetByIdAsync(UserId, id, IsAdmin, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<OrderDetailDto>.Ok(result.Value))
            : NotFound(ApiResponse<OrderDetailDto>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelOrderRequest request, CancellationToken cancellationToken)
    {
        var result = await _orderService.CancelOrderAsync(UserId, id, request, cancellationToken);
        return result.IsSuccess ? Ok(ApiResponse<object>.Ok(new { })) : BadRequest(ApiResponse<object>.Fail(result.Error!, result.ErrorCode));
    }

    // ---- Admin ----

    [HttpGet("admin/all")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetAll([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var result = await _orderService.GetAllAsync(pageNumber, pageSize, cancellationToken);
        return Ok(ApiResponse<PagedResult<OrderListItemDto>>.Ok(result.Value));
    }

    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateOrderStatusRequest request, CancellationToken cancellationToken)
    {
        var result = await _orderService.UpdateStatusAsync(id, request, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<OrderDetailDto>.Ok(result.Value))
            : NotFound(ApiResponse<OrderDetailDto>.Fail(result.Error!, result.ErrorCode));
    }
}

/// <summary>
/// Receives Stripe webhook events. Not [Authorize] — Stripe calls this anonymously — but every request
/// is verified against the raw payload signature via <see cref="IPaymentService.ConfirmWebhookSignatureAsync"/>
/// before anything is trusted, so an attacker cannot forge a "payment succeeded" event.
/// </summary>
[ApiController]
[Route("api/v1/payments/webhook")]
[DisableRateLimiting]
public class PaymentsWebhookController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly ICheckoutService _checkoutService;
    private readonly ILogger<PaymentsWebhookController> _logger;

    public PaymentsWebhookController(IPaymentService paymentService, ICheckoutService checkoutService, ILogger<PaymentsWebhookController> logger)
    {
        _paymentService = paymentService;
        _checkoutService = checkoutService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> HandleStripeWebhook(CancellationToken cancellationToken)
    {
        var json = await new StreamReader(Request.Body).ReadToEndAsync(cancellationToken);
        var signature = Request.Headers["Stripe-Signature"].ToString();

        var isValid = await _paymentService.ConfirmWebhookSignatureAsync(json, signature);
        if (!isValid)
        {
            // Logged (not returned to the caller) — the response body stays generic so a malicious
            // request can't learn *why* verification failed, but the real reason (usually a stale/
            // mismatched Stripe:WebhookSecret) needs to be visible somewhere for debugging.
            _logger.LogWarning(
                "Stripe webhook signature verification failed. This almost always means the configured " +
                "Stripe:WebhookSecret doesn't match the one `stripe listen` is currently using — restart the " +
                "API after updating appsettings.Local.json. Signature header present: {HasSignature}",
                !string.IsNullOrEmpty(signature));
            return BadRequest();
        }

        _logger.LogInformation("Stripe webhook verified successfully.");

        var stripeEvent = EventUtility.ParseEvent(json);

        switch (stripeEvent.Type)
        {
            case "payment_intent.succeeded":
            {
                var intent = stripeEvent.Data.Object as PaymentIntent;
                if (intent is not null)
                {
                    await _checkoutService.HandlePaymentSucceededAsync(intent.Id, cancellationToken);
                }
                break;
            }
            case "payment_intent.payment_failed":
            {
                var intent = stripeEvent.Data.Object as PaymentIntent;
                if (intent is not null)
                {
                    var reason = intent.LastPaymentError?.Message ?? "Payment failed.";
                    await _checkoutService.HandlePaymentFailedAsync(intent.Id, reason, cancellationToken);
                }
                break;
            }
        }

        return Ok();
    }
}
