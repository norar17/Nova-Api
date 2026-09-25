using ECommerce.Domain.Common;
using ECommerce.Domain.Enums;

namespace ECommerce.Domain.Entities;

/// <summary>
/// A merchant's shop/storefront. Every product belongs to exactly one store.
/// A user becomes a merchant by creating a store (see StoreService.CreateAsync), which also
/// grants them the "Merchant" role. New stores start Pending and must be Approved by an Admin
/// before their products become visible in the public catalog.
/// </summary>
public class Store : BaseEntity
{
    public Guid OwnerId { get; set; }
    public ApplicationUser Owner { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? LogoUrl { get; set; }
    public string? LogoPublicId { get; set; }
    public string? BannerUrl { get; set; }

    public StoreStatus Status { get; set; } = StoreStatus.Pending;
    public string? RejectionReason { get; set; }

    public double AverageRating { get; set; }
    public int TotalSales { get; set; }

    public ICollection<Product> Products { get; set; } = new List<Product>();
}
