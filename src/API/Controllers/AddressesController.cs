using System.Security.Claims;
using ECommerce.Application.Addresses.DTOs;
using ECommerce.Application.Addresses.Interfaces;
using ECommerce.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerce.API.Controllers;

[ApiController]
[Route("api/v1/addresses")]
[Authorize]
public class AddressesController : ControllerBase
{
    private readonly IAddressService _addressService;
    public AddressesController(IAddressService addressService) => _addressService = addressService;

    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var result = await _addressService.GetAllAsync(UserId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<AddressDto>>.Ok(result.Value));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UpsertAddressRequest request, CancellationToken cancellationToken)
    {
        var result = await _addressService.CreateAsync(UserId, request, cancellationToken);
        return Ok(ApiResponse<AddressDto>.Ok(result.Value));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpsertAddressRequest request, CancellationToken cancellationToken)
    {
        var result = await _addressService.UpdateAsync(UserId, id, request, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<AddressDto>.Ok(result.Value))
            : NotFound(ApiResponse<AddressDto>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _addressService.DeleteAsync(UserId, id, cancellationToken);
        return result.IsSuccess ? NoContent() : NotFound(ApiResponse<object>.Fail(result.Error!, result.ErrorCode));
    }
}
