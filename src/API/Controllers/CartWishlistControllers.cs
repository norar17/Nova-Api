using System.Security.Claims;
using ECommerce.Application.Cart.DTOs;
using ECommerce.Application.Cart.Interfaces;
using ECommerce.Application.Wishlist.DTOs;
using ECommerce.Application.Wishlist.Interfaces;
using ECommerce.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerce.API.Controllers;

[ApiController]
[Route("api/v1/cart")]
[Authorize]
public class CartController : ControllerBase
{
    private readonly ICartService _cartService;
    public CartController(ICartService cartService) => _cartService = cartService;

    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> GetCart(CancellationToken cancellationToken)
    {
        var result = await _cartService.GetCartAsync(UserId, cancellationToken);
        return Ok(ApiResponse<CartDto>.Ok(result.Value));
    }

    [HttpPost("items")]
    public async Task<IActionResult> AddItem([FromBody] AddToCartRequest request, CancellationToken cancellationToken)
    {
        var result = await _cartService.AddItemAsync(UserId, request, cancellationToken);
        return result.IsSuccess ? Ok(ApiResponse<CartDto>.Ok(result.Value)) : BadRequest(ApiResponse<CartDto>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpPut("items/{cartItemId:guid}")]
    public async Task<IActionResult> UpdateItem(Guid cartItemId, [FromBody] UpdateCartItemRequest request, CancellationToken cancellationToken)
    {
        var result = await _cartService.UpdateItemAsync(UserId, cartItemId, request, cancellationToken);
        return result.IsSuccess ? Ok(ApiResponse<CartDto>.Ok(result.Value)) : BadRequest(ApiResponse<CartDto>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpDelete("items/{cartItemId:guid}")]
    public async Task<IActionResult> RemoveItem(Guid cartItemId, CancellationToken cancellationToken)
    {
        var result = await _cartService.RemoveItemAsync(UserId, cartItemId, cancellationToken);
        return result.IsSuccess ? Ok(ApiResponse<CartDto>.Ok(result.Value)) : NotFound(ApiResponse<CartDto>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpDelete]
    public async Task<IActionResult> Clear(CancellationToken cancellationToken)
    {
        await _cartService.ClearCartAsync(UserId, cancellationToken);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/wishlist")]
[Authorize]
public class WishlistController : ControllerBase
{
    private readonly IWishlistService _wishlistService;
    public WishlistController(IWishlistService wishlistService) => _wishlistService = wishlistService;

    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var result = await _wishlistService.GetAsync(UserId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<WishlistItemDto>>.Ok(result.Value));
    }

    [HttpPost("{productId:guid}")]
    public async Task<IActionResult> Add(Guid productId, CancellationToken cancellationToken)
    {
        var result = await _wishlistService.AddAsync(UserId, productId, cancellationToken);
        return result.IsSuccess ? Ok(ApiResponse<object>.Ok(new { })) : NotFound(ApiResponse<object>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpDelete("{productId:guid}")]
    public async Task<IActionResult> Remove(Guid productId, CancellationToken cancellationToken)
    {
        await _wishlistService.RemoveAsync(UserId, productId, cancellationToken);
        return NoContent();
    }
}
