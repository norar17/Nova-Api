using ECommerce.Application.Catalog.DTOs;
using ECommerce.Application.Catalog.Interfaces;
using ECommerce.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerce.API.Controllers;

[ApiController]
[Route("api/v1/categories")]
public class CategoriesController : ControllerBase
{
    private readonly ICategoryService _categoryService;
    public CategoriesController(ICategoryService categoryService) => _categoryService = categoryService;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var result = await _categoryService.GetAllAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<CategoryDto>>.Ok(result.Value));
    }

    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Create([FromBody] CreateCategoryRequest request, CancellationToken cancellationToken)
    {
        var result = await _categoryService.CreateAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(ApiResponse<CategoryDto>.Ok(result.Value)) : BadRequest(ApiResponse<CategoryDto>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCategoryRequest request, CancellationToken cancellationToken)
    {
        var result = await _categoryService.UpdateAsync(id, request, cancellationToken);
        return result.IsSuccess ? Ok(ApiResponse<CategoryDto>.Ok(result.Value)) : NotFound(ApiResponse<CategoryDto>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _categoryService.SoftDeleteAsync(id, cancellationToken);
        return result.IsSuccess ? NoContent() : NotFound(ApiResponse<object>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpPost("{id:guid}/restore")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Restore(Guid id, CancellationToken cancellationToken)
    {
        var result = await _categoryService.RestoreAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(ApiResponse<object>.Ok(new { })) : NotFound(ApiResponse<object>.Fail(result.Error!, result.ErrorCode));
    }
}

[ApiController]
[Route("api/v1/brands")]
public class BrandsController : ControllerBase
{
    private readonly IBrandService _brandService;
    public BrandsController(IBrandService brandService) => _brandService = brandService;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var result = await _brandService.GetAllAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<BrandDto>>.Ok(result.Value));
    }

    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Create([FromBody] CreateBrandRequest request, CancellationToken cancellationToken)
    {
        var result = await _brandService.CreateAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(ApiResponse<BrandDto>.Ok(result.Value)) : BadRequest(ApiResponse<BrandDto>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateBrandRequest request, CancellationToken cancellationToken)
    {
        var result = await _brandService.UpdateAsync(id, request, cancellationToken);
        return result.IsSuccess ? Ok(ApiResponse<BrandDto>.Ok(result.Value)) : NotFound(ApiResponse<BrandDto>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _brandService.SoftDeleteAsync(id, cancellationToken);
        return result.IsSuccess ? NoContent() : NotFound(ApiResponse<object>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpPost("{id:guid}/restore")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Restore(Guid id, CancellationToken cancellationToken)
    {
        var result = await _brandService.RestoreAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(ApiResponse<object>.Ok(new { })) : NotFound(ApiResponse<object>.Fail(result.Error!, result.ErrorCode));
    }
}
