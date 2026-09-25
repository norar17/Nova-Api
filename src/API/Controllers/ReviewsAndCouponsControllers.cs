using System.Security.Claims;
using ECommerce.Application.Coupons.DTOs;
using ECommerce.Application.Coupons.Interfaces;
using ECommerce.Application.Reviews.DTOs;
using ECommerce.Application.Reviews.Interfaces;
using ECommerce.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerce.API.Controllers;

[ApiController]
[Route("api/v1/products/{productId:guid}/reviews")]
public class ReviewsController : ControllerBase
{
    private readonly IReviewService _reviewService;
    public ReviewsController(IReviewService reviewService) => _reviewService = reviewService;

    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private bool IsAdmin => User.IsInRole("Admin");

    [HttpGet]
    public async Task<IActionResult> GetForProduct(Guid productId, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10, CancellationToken cancellationToken = default)
    {
        var result = await _reviewService.GetForProductAsync(productId, pageNumber, pageSize, cancellationToken);
        return Ok(ApiResponse<PagedResult<ReviewDto>>.Ok(result.Value));
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Create(Guid productId, [FromBody] CreateReviewRequest request, CancellationToken cancellationToken)
    {
        var result = await _reviewService.CreateAsync(UserId, productId, request, cancellationToken);
        return result.IsSuccess ? Ok(ApiResponse<ReviewDto>.Ok(result.Value)) : BadRequest(ApiResponse<ReviewDto>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpDelete("{reviewId:guid}")]
    [Authorize]
    public async Task<IActionResult> Delete(Guid productId, Guid reviewId, CancellationToken cancellationToken)
    {
        var result = await _reviewService.DeleteAsync(UserId, reviewId, IsAdmin, cancellationToken);
        return result.IsSuccess ? NoContent() : BadRequest(ApiResponse<object>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpPost("{reviewId:guid}/helpful")]
    public async Task<IActionResult> MarkHelpful(Guid productId, Guid reviewId, CancellationToken cancellationToken)
    {
        var result = await _reviewService.MarkHelpfulAsync(reviewId, cancellationToken);
        return result.IsSuccess ? Ok(ApiResponse<object>.Ok(new { })) : NotFound(ApiResponse<object>.Fail(result.Error!, result.ErrorCode));
    }
}

[ApiController]
[Route("api/v1/coupons")]
[Authorize(Policy = "AdminOnly")]
public class CouponsController : ControllerBase
{
    private readonly ICouponService _couponService;
    public CouponsController(ICouponService couponService) => _couponService = couponService;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var result = await _couponService.GetAllAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<CouponDto>>.Ok(result.Value));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCouponRequest request, CancellationToken cancellationToken)
    {
        var result = await _couponService.CreateAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(ApiResponse<CouponDto>.Ok(result.Value)) : BadRequest(ApiResponse<CouponDto>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCouponRequest request, CancellationToken cancellationToken)
    {
        var result = await _couponService.UpdateAsync(id, request, cancellationToken);
        return result.IsSuccess ? Ok(ApiResponse<CouponDto>.Ok(result.Value)) : NotFound(ApiResponse<CouponDto>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _couponService.DeleteAsync(id, cancellationToken);
        return result.IsSuccess ? NoContent() : NotFound(ApiResponse<object>.Fail(result.Error!, result.ErrorCode));
    }
}
