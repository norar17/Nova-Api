using System.Reflection;
using ECommerce.Application.Addresses.Interfaces;
using ECommerce.Application.Addresses.Services;
using ECommerce.Application.Auth.Interfaces;
using ECommerce.Application.Auth.Services;
using ECommerce.Application.Cart.Interfaces;
using ECommerce.Application.Cart.Services;
using ECommerce.Application.Catalog.Interfaces;
using ECommerce.Application.Catalog.Services;
using ECommerce.Application.Chat.Interfaces;
using ECommerce.Application.Chat.Services;
using ECommerce.Application.Coupons.Interfaces;
using ECommerce.Application.Coupons.Services;
using ECommerce.Application.Orders.Interfaces;
using ECommerce.Application.Orders.Services;
using ECommerce.Application.Reviews.Interfaces;
using ECommerce.Application.Reviews.Services;
using ECommerce.Application.Stores.Interfaces;
using ECommerce.Application.Stores.Services;
using ECommerce.Application.Wishlist.Interfaces;
using ECommerce.Application.Wishlist.Services;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace ECommerce.Application.DependencyInjection;

public static class ApplicationServiceRegistration
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddAutoMapper(assembly);
        services.AddValidatorsFromAssembly(assembly);

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IAddressService, AddressService>();

        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IBrandService, BrandService>();

        services.AddScoped<ICartService, CartService>();
        services.AddScoped<IWishlistService, WishlistService>();

        services.AddScoped<ICheckoutService, CheckoutService>();
        services.AddScoped<IOrderService, OrderService>();

        services.AddScoped<IReviewService, ReviewService>();
        services.AddScoped<ICouponService, CouponService>();
        services.AddScoped<IStoreService, StoreService>();
        services.AddScoped<IChatService, ChatService>();

        return services;
    }
}
