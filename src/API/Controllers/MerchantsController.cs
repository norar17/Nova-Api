using System.Security.Claims;
using ECommerce.Application.Catalog.DTOs;
using ECommerce.Application.Catalog.Interfaces;
using ECommerce.Application.Orders.DTOs;
using ECommerce.Application.Orders.Interfaces;
using ECommerce.Application.Stores.DTOs;
using ECommerce.Application.Stores.Interfaces;
using ECommerce.Domain.Enums;
using ECommerce.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerce.API.Controllers;

/// <summary>
/// Anyone authenticated can become a merchant by creating a store here — there's no separate
/// "apply to sell" approval gate before that point, only before their products go live publicly
/// (see StoreStatus.Pending vs Approved, enforced in ProductService's public queries).
/// </summary>
[ApiController]
[Route("api/v1/merchants")]
[Authorize]
public class MerchantsController : ControllerBase
{
    private readonly IStoreService _storeService;
    private readonly IProductService _productService;
    private readonly IOrderService _orderService;

    public MerchantsController(IStoreService storeService, IProductService productService, IOrderService orderService)
    {
        _storeService = storeService;
        _productService = productService;
        _orderService = orderService;
    }

    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("store")]
    public async Task<IActionResult> GetMyStore(CancellationToken cancellationToken)
    {
        var result = await _storeService.GetMyStoreAsync(UserId, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<StoreDto>.Ok(result.Value))
            : NotFound(ApiResponse<StoreDto>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpPost("store")]
    public async Task<IActionResult> CreateStore([FromBody] CreateStoreRequest request, CancellationToken cancellationToken)
    {
        var result = await _storeService.CreateAsync(UserId, request, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<StoreDto>.Ok(result.Value, "Store created — pending admin approval."))
            : BadRequest(ApiResponse<StoreDto>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpPut("store")]
    public async Task<IActionResult> UpdateStore([FromBody] UpdateStoreRequest request, CancellationToken cancellationToken)
    {
        var result = await _storeService.UpdateAsync(UserId, request, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<StoreDto>.Ok(result.Value))
            : BadRequest(ApiResponse<StoreDto>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpGet("store/products")]
    public async Task<IActionResult> GetMyProducts([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var storeResult = await _storeService.GetMyStoreAsync(UserId, cancellationToken);
        if (storeResult.IsFailure)
        {
            return NotFound(ApiResponse<object>.Fail(storeResult.Error!, storeResult.ErrorCode));
        }

        var result = await _productService.GetByStoreAsync(storeResult.Value.Id, pageNumber, pageSize, cancellationToken);
        return Ok(ApiResponse<PagedResult<ProductListItemDto>>.Ok(result.Value));
    }

    [HttpPost("store/products")]
    public async Task<IActionResult> CreateProduct([FromBody] CreateProductRequest request, CancellationToken cancellationToken)
    {
        var ownsResult = await _storeService.OwnsStoreAsync(UserId, request.StoreId, cancellationToken);
        if (!ownsResult.Value && !User.IsInRole("Admin"))
        {
            return Forbid();
        }

        var result = await _productService.CreateAsync(request, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<ProductDetailDto>.Ok(result.Value))
            : BadRequest(ApiResponse<ProductDetailDto>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpPut("store/products/{id:guid}")]
    public async Task<IActionResult> UpdateProduct(Guid id, [FromBody] UpdateProductRequest request, CancellationToken cancellationToken)
    {
        var existing = await _productService.GetByIdAsync(id, cancellationToken);
        if (existing.IsFailure)
        {
            return NotFound(ApiResponse<object>.Fail(existing.Error!, existing.ErrorCode));
        }

        var ownsResult = await _storeService.OwnsStoreAsync(UserId, existing.Value.StoreId, cancellationToken);
        if (!ownsResult.Value && !User.IsInRole("Admin"))
        {
            return Forbid();
        }

        var result = await _productService.UpdateAsync(id, request, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<ProductDetailDto>.Ok(result.Value))
            : BadRequest(ApiResponse<ProductDetailDto>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpDelete("store/products/{id:guid}")]
    public async Task<IActionResult> DeleteProduct(Guid id, CancellationToken cancellationToken)
    {
        var existing = await _productService.GetByIdAsync(id, cancellationToken);
        if (existing.IsFailure)
        {
            return NotFound(ApiResponse<object>.Fail(existing.Error!, existing.ErrorCode));
        }

        var ownsResult = await _storeService.OwnsStoreAsync(UserId, existing.Value.StoreId, cancellationToken);
        if (!ownsResult.Value && !User.IsInRole("Admin"))
        {
            return Forbid();
        }

        var result = await _productService.SoftDeleteAsync(id, cancellationToken);
        return result.IsSuccess ? NoContent() : BadRequest(ApiResponse<object>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpPost("store/products/{id:guid}/variants")]
    public async Task<IActionResult> AddProductVariant(Guid id, [FromBody] CreateProductVariantRequest request, CancellationToken cancellationToken)
    {
        var existing = await _productService.GetByIdAsync(id, cancellationToken);
        if (existing.IsFailure)
        {
            return NotFound(ApiResponse<object>.Fail(existing.Error!, existing.ErrorCode));
        }

        var ownsResult = await _storeService.OwnsStoreAsync(UserId, existing.Value.StoreId, cancellationToken);
        if (!ownsResult.Value && !User.IsInRole("Admin"))
        {
            return Forbid();
        }

        var result = await _productService.AddVariantAsync(id, request, cancellationToken);
        return result.IsSuccess ? Ok(ApiResponse<object>.Ok(new { })) : BadRequest(ApiResponse<object>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpPost("store/products/{id:guid}/images")]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> UploadProductImage(Guid id, IFormFile file, [FromForm] bool isThumbnail, CancellationToken cancellationToken)
    {
        var existing = await _productService.GetByIdAsync(id, cancellationToken);
        if (existing.IsFailure)
        {
            return NotFound(ApiResponse<object>.Fail(existing.Error!, existing.ErrorCode));
        }

        var ownsResult = await _storeService.OwnsStoreAsync(UserId, existing.Value.StoreId, cancellationToken);
        if (!ownsResult.Value && !User.IsInRole("Admin"))
        {
            return Forbid();
        }

        if (file.Length == 0)
        {
            return BadRequest(ApiResponse<object>.Fail("No file was uploaded.", "EMPTY_FILE"));
        }

        await using var stream = file.OpenReadStream();
        var result = await _productService.AddImageAsync(id, stream, file.FileName, isThumbnail, cancellationToken);
        return result.IsSuccess ? Ok(ApiResponse<object>.Ok(new { })) : BadRequest(ApiResponse<object>.Fail(result.Error!, result.ErrorCode));
    }

    // ---- Orders (seller's own store) ----

    [HttpGet("store/orders")]
    public async Task<IActionResult> GetMyStoreOrders([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var storeResult = await _storeService.GetMyStoreAsync(UserId, cancellationToken);
        if (storeResult.IsFailure)
        {
            return NotFound(ApiResponse<object>.Fail(storeResult.Error!, storeResult.ErrorCode));
        }

        var result = await _orderService.GetByStoreAsync(storeResult.Value.Id, pageNumber, pageSize, cancellationToken);
        return Ok(ApiResponse<PagedResult<OrderListItemDto>>.Ok(result.Value));
    }

    [HttpGet("store/orders/{id:guid}")]
    public async Task<IActionResult> GetMyStoreOrder(Guid id, CancellationToken cancellationToken)
    {
        var storeResult = await _storeService.GetMyStoreAsync(UserId, cancellationToken);
        if (storeResult.IsFailure)
        {
            return NotFound(ApiResponse<object>.Fail(storeResult.Error!, storeResult.ErrorCode));
        }

        var accessResult = await _orderService.StoreHasAccessToOrderAsync(storeResult.Value.Id, id, cancellationToken);
        if (!accessResult.Value)
        {
            return Forbid();
        }

        var result = await _orderService.GetByIdAsync(UserId, id, isAdmin: true, cancellationToken); // isAdmin:true bypasses the buyer-only ownership check — seller access was already verified above
        return result.IsSuccess
            ? Ok(ApiResponse<OrderDetailDto>.Ok(result.Value))
            : NotFound(ApiResponse<OrderDetailDto>.Fail(result.Error!, result.ErrorCode));
    }

    /// <summary>
    /// A seller can update the status of any order containing at least one of their store's items.
    /// Note: if an order mixes items from multiple stores (a real possibility on a marketplace where
    /// checkout aggregates the whole cart into one order), any of those sellers updating status
    /// changes the *entire* order — there's no per-seller split-shipment status yet. Flagged here
    /// rather than silently assumed away; a future iteration could move Status onto OrderItem instead.
    /// </summary>
    [HttpPatch("store/orders/{id:guid}/status")]
    public async Task<IActionResult> UpdateMyStoreOrderStatus(Guid id, [FromBody] UpdateOrderStatusRequest request, CancellationToken cancellationToken)
    {
        var storeResult = await _storeService.GetMyStoreAsync(UserId, cancellationToken);
        if (storeResult.IsFailure)
        {
            return NotFound(ApiResponse<object>.Fail(storeResult.Error!, storeResult.ErrorCode));
        }

        var accessResult = await _orderService.StoreHasAccessToOrderAsync(storeResult.Value.Id, id, cancellationToken);
        if (!accessResult.Value)
        {
            return Forbid();
        }

        var result = await _orderService.UpdateStatusAsync(id, request, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<OrderDetailDto>.Ok(result.Value))
            : NotFound(ApiResponse<OrderDetailDto>.Fail(result.Error!, result.ErrorCode));
    }

    // ---- Admin: store approval ----

    [HttpGet("admin/stores")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetAllStores([FromQuery] StoreStatus? status, CancellationToken cancellationToken)
    {
        var result = await _storeService.GetAllAsync(status, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<StoreDto>>.Ok(result.Value));
    }

    [HttpPost("admin/stores/{id:guid}/approve")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> ApproveStore(Guid id, CancellationToken cancellationToken)
    {
        var result = await _storeService.ApproveAsync(id, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<StoreDto>.Ok(result.Value))
            : NotFound(ApiResponse<StoreDto>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpPost("admin/stores/{id:guid}/reject")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> RejectStore(Guid id, [FromBody] RejectStoreRequest request, CancellationToken cancellationToken)
    {
        var result = await _storeService.RejectAsync(id, request, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<StoreDto>.Ok(result.Value))
            : NotFound(ApiResponse<StoreDto>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpPost("admin/stores/{id:guid}/suspend")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> SuspendStore(Guid id, CancellationToken cancellationToken)
    {
        var result = await _storeService.SuspendAsync(id, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<StoreDto>.Ok(result.Value))
            : NotFound(ApiResponse<StoreDto>.Fail(result.Error!, result.ErrorCode));
    }
}

[ApiController]
[Route("api/v1/stores")]
public class PublicStoresController : ControllerBase
{
    private readonly IStoreService _storeService;
    public PublicStoresController(IStoreService storeService) => _storeService = storeService;

    [HttpGet("{slug}")]
    public async Task<IActionResult> GetBySlug(string slug, CancellationToken cancellationToken)
    {
        var result = await _storeService.GetBySlugAsync(slug, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<StoreDto>.Ok(result.Value))
            : NotFound(ApiResponse<StoreDto>.Fail(result.Error!, result.ErrorCode));
    }
}
