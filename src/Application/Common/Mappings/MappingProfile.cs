using AutoMapper;
using ECommerce.Application.Auth.DTOs;
using ECommerce.Domain.Entities;

namespace ECommerce.Application.Common.Mappings;

/// <summary>
/// Central AutoMapper profile. As Products/Orders/Cart/Reviews services are added (Phase 3),
/// their mappings are appended here rather than creating one profile class per feature —
/// AutoMapper only needs a single profile registered per assembly.
/// </summary>
public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<ApplicationUser, UserProfileDto>()
            .ForMember(dest => dest.Roles, opt => opt.Ignore()); // populated manually via UserManager.GetRolesAsync
    }
}
