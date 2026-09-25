using ECommerce.Application.Catalog.DTOs;
using ECommerce.Application.Catalog.Interfaces;
using ECommerce.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerce.API.Controllers;

[ApiController]
[Route("api/v1/products")]
public class ProductsController : ControllerBase
{
    private readonly IProductService _productService;

    public ProductsController(IProductService productService) => _productService = productService;

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] ProductQueryParams query, CancellationToken cancellationToken)
    {
        var result = await _productService.SearchAsync(query, cancellationToken);
        return Ok(ApiResponse<PagedResult<ProductListItemDto>>.Ok(result.Value));
    }

    [HttpGet("featured")]
    public async Task<IActionResult> Featured(CancellationToken cancellationToken)
    {
        var result = await _productService.GetFeaturedAsync(cancellationToken: cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<ProductListItemDto>>.Ok(result.Value));
    }

    [HttpGet("trending")]
    public async Task<IActionResult> Trending(CancellationToken cancellationToken)
    {
        var result = await _productService.GetTrendingAsync(cancellationToken: cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<ProductListItemDto>>.Ok(result.Value));
    }

    [HttpGet("new-arrivals")]
    public async Task<IActionResult> NewArrivals(CancellationToken cancellationToken)
    {
        var result = await _productService.GetNewArrivalsAsync(cancellationToken: cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<ProductListItemDto>>.Ok(result.Value));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _productService.GetByIdAsync(id, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<ProductDetailDto>.Ok(result.Value))
            : NotFound(ApiResponse<ProductDetailDto>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpGet("slug/{slug}")]
    public async Task<IActionResult> GetBySlug(string slug, CancellationToken cancellationToken)
    {
        var result = await _productService.GetBySlugAsync(slug, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<ProductDetailDto>.Ok(result.Value))
            : NotFound(ApiResponse<ProductDetailDto>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpGet("{id:guid}/related")]
    public async Task<IActionResult> Related(Guid id, CancellationToken cancellationToken)
    {
        var result = await _productService.GetRelatedAsync(id, cancellationToken: cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<IReadOnlyList<ProductListItemDto>>.Ok(result.Value))
            : NotFound(ApiResponse<IReadOnlyList<ProductListItemDto>>.Fail(result.Error!, result.ErrorCode));
    }

    // ---- Admin ----

    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Create([FromBody] CreateProductRequest request, CancellationToken cancellationToken)
    {
        var result = await _productService.CreateAsync(request, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, ApiResponse<ProductDetailDto>.Ok(result.Value))
            : BadRequest(ApiResponse<ProductDetailDto>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateProductRequest request, CancellationToken cancellationToken)
    {
        var result = await _productService.UpdateAsync(id, request, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<ProductDetailDto>.Ok(result.Value))
            : BadRequest(ApiResponse<ProductDetailDto>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _productService.SoftDeleteAsync(id, cancellationToken);
        return result.IsSuccess ? NoContent() : NotFound(ApiResponse<object>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpPost("{id:guid}/restore")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Restore(Guid id, CancellationToken cancellationToken)
    {
        var result = await _productService.RestoreAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(ApiResponse<object>.Ok(new { })) : NotFound(ApiResponse<object>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpPost("{id:guid}/variants")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> AddVariant(Guid id, [FromBody] CreateProductVariantRequest request, CancellationToken cancellationToken)
    {
        var result = await _productService.AddVariantAsync(id, request, cancellationToken);
        return result.IsSuccess ? Ok(ApiResponse<object>.Ok(new { })) : BadRequest(ApiResponse<object>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpPost("{id:guid}/images")]
    [Authorize(Policy = "AdminOnly")]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> UploadImage(Guid id, IFormFile file, [FromForm] bool isThumbnail, CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            return BadRequest(ApiResponse<object>.Fail("No file was uploaded.", "EMPTY_FILE"));
        }

        await using var stream = file.OpenReadStream();
        var result = await _productService.AddImageAsync(id, stream, file.FileName, isThumbnail, cancellationToken);
        return result.IsSuccess ? Ok(ApiResponse<object>.Ok(new { })) : BadRequest(ApiResponse<object>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpDelete("{id:guid}/images/{imageId:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> DeleteImage(Guid id, Guid imageId, CancellationToken cancellationToken)
    {
        var result = await _productService.DeleteImageAsync(id, imageId, cancellationToken);
        return result.IsSuccess ? NoContent() : NotFound(ApiResponse<object>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpPatch("{id:guid}/stock")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> AdjustStock(Guid id, [FromQuery] Guid? variantId, [FromQuery] int delta, CancellationToken cancellationToken)
    {
        var result = await _productService.AdjustStockAsync(id, variantId, delta, cancellationToken);
        return result.IsSuccess ? Ok(ApiResponse<object>.Ok(new { })) : BadRequest(ApiResponse<object>.Fail(result.Error!, result.ErrorCode));
    }
}
