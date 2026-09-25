using ECommerce.Domain.Entities;
using ECommerce.Domain.Enums;
using ECommerce.Domain.Interfaces;
using ECommerce.Shared;
using FluentValidation;
using MongoDB.Driver.Linq;

namespace ECommerce.Application.Addresses.DTOs
{
    public record AddressDto(
        Guid Id, AddressType Type, string FullName, string PhoneNumber,
        string Line1, string? Line2, string City, string State, string PostalCode, string Country, bool IsDefault);

    public record UpsertAddressRequest(
        AddressType Type, string FullName, string PhoneNumber,
        string Line1, string? Line2, string City, string State, string PostalCode, string Country, bool IsDefault);
}

namespace ECommerce.Application.Addresses.Interfaces
{
    using ECommerce.Application.Addresses.DTOs;

    public interface IAddressService
    {
        Task<Result<IReadOnlyList<AddressDto>>> GetAllAsync(Guid userId, CancellationToken cancellationToken = default);
        Task<Result<AddressDto>> CreateAsync(Guid userId, UpsertAddressRequest request, CancellationToken cancellationToken = default);
        Task<Result<AddressDto>> UpdateAsync(Guid userId, Guid addressId, UpsertAddressRequest request, CancellationToken cancellationToken = default);
        Task<Result> DeleteAsync(Guid userId, Guid addressId, CancellationToken cancellationToken = default);
    }
}

namespace ECommerce.Application.Addresses.Validators
{
    using ECommerce.Application.Addresses.DTOs;

    public class UpsertAddressRequestValidator : AbstractValidator<UpsertAddressRequest>
    {
        public UpsertAddressRequestValidator()
        {
            RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
            RuleFor(x => x.PhoneNumber).NotEmpty().MaximumLength(30);
            RuleFor(x => x.Line1).NotEmpty().MaximumLength(200);
            RuleFor(x => x.City).NotEmpty().MaximumLength(100);
            RuleFor(x => x.State).NotEmpty().MaximumLength(100);
            RuleFor(x => x.PostalCode).NotEmpty().MaximumLength(20);
            RuleFor(x => x.Country).NotEmpty().MaximumLength(100);
        }
    }
}

namespace ECommerce.Application.Addresses.Services
{
    using ECommerce.Application.Addresses.DTOs;
    using ECommerce.Application.Addresses.Interfaces;

    public class AddressService : IAddressService
    {
        private readonly IUnitOfWork _unitOfWork;

        public AddressService(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

        public async Task<Result<IReadOnlyList<AddressDto>>> GetAllAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var addresses = await _unitOfWork.Repository<Address>().Query()
                .Where(a => a.UserId == userId)
                .OrderByDescending(a => a.IsDefault)
                .ThenByDescending(a => a.CreatedAtUtc)
                .ToListAsync(cancellationToken);

            return Result.Success<IReadOnlyList<AddressDto>>(addresses.Select(ToDto).ToList());
        }

        public async Task<Result<AddressDto>> CreateAsync(Guid userId, UpsertAddressRequest request, CancellationToken cancellationToken = default)
        {
            if (request.IsDefault)
            {
                await ClearExistingDefaultAsync(userId, cancellationToken);
            }

            var address = new Address
            {
                UserId = userId,
                Type = request.Type,
                FullName = request.FullName,
                PhoneNumber = request.PhoneNumber,
                Line1 = request.Line1,
                Line2 = request.Line2,
                City = request.City,
                State = request.State,
                PostalCode = request.PostalCode,
                Country = request.Country,
                IsDefault = request.IsDefault,
            };

            await _unitOfWork.Repository<Address>().AddAsync(address, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(ToDto(address));
        }

        public async Task<Result<AddressDto>> UpdateAsync(Guid userId, Guid addressId, UpsertAddressRequest request, CancellationToken cancellationToken = default)
        {
            var address = await _unitOfWork.Repository<Address>().Query(asNoTracking: false)
                .FirstOrDefaultAsync(a => a.Id == addressId && a.UserId == userId, cancellationToken);

            if (address is null)
            {
                return Result.NotFound<AddressDto>("Address", addressId);
            }

            if (request.IsDefault && !address.IsDefault)
            {
                await ClearExistingDefaultAsync(userId, cancellationToken);
            }

            address.Type = request.Type;
            address.FullName = request.FullName;
            address.PhoneNumber = request.PhoneNumber;
            address.Line1 = request.Line1;
            address.Line2 = request.Line2;
            address.City = request.City;
            address.State = request.State;
            address.PostalCode = request.PostalCode;
            address.Country = request.Country;
            address.IsDefault = request.IsDefault;

            _unitOfWork.Repository<Address>().Update(address);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(ToDto(address));
        }

        public async Task<Result> DeleteAsync(Guid userId, Guid addressId, CancellationToken cancellationToken = default)
        {
            var address = await _unitOfWork.Repository<Address>().Query(asNoTracking: false)
                .FirstOrDefaultAsync(a => a.Id == addressId && a.UserId == userId, cancellationToken);

            if (address is null)
            {
                return Result.NotFound("Address", addressId);
            }

            _unitOfWork.Repository<Address>().Remove(address); // addresses carry no financial/audit history — safe to hard delete
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }

        private async Task ClearExistingDefaultAsync(Guid userId, CancellationToken cancellationToken)
        {
            var currentDefault = await _unitOfWork.Repository<Address>().Query(asNoTracking: false)
                .FirstOrDefaultAsync(a => a.UserId == userId && a.IsDefault, cancellationToken);

            if (currentDefault is not null)
            {
                currentDefault.IsDefault = false;
                _unitOfWork.Repository<Address>().Update(currentDefault);
            }
        }

        private static AddressDto ToDto(Address a) => new(
            a.Id, a.Type, a.FullName, a.PhoneNumber, a.Line1, a.Line2, a.City, a.State, a.PostalCode, a.Country, a.IsDefault);
    }
}
