using ECommerce.Application.Common.Interfaces;
using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Interfaces;
using ECommerce.Infrastructure.Auth;
using ECommerce.Infrastructure.Auth.Identity;
using ECommerce.Infrastructure.Email;
using ECommerce.Infrastructure.Payments;
using ECommerce.Infrastructure.Persistence;
using ECommerce.Infrastructure.Persistence.Mongo;
using ECommerce.Infrastructure.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace ECommerce.Infrastructure.DependencyInjection;

public static class InfrastructureServiceRegistration
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Registers all the BsonClassMap ignores (navigation properties) and the Guid
        // representation. Must run before any Mongo driver serialization happens, so it's called
        // here at service-registration time rather than lazily on first use.
        BsonMappings.Register();

        var mongoOptions = configuration.GetSection(MongoOptions.SectionName).Get<MongoOptions>()
            ?? throw new InvalidOperationException("MongoDb configuration section is missing.");

        if (string.IsNullOrWhiteSpace(mongoOptions.ConnectionString))
        {
            throw new InvalidOperationException("MongoDb:ConnectionString is not configured.");
        }

        services.AddSingleton(mongoOptions);
        services.AddSingleton<IMongoClient>(_ => new MongoClient(mongoOptions.ConnectionString));
        services.AddSingleton<MongoDbContext>();

        services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
            {
                options.Password.RequiredLength = 8;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireDigit = true;
                options.Password.RequireNonAlphanumeric = true;

                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = true;
            })
            // No .AddEntityFrameworkStores<T>() — that's the EF-specific registration helper.
            // Instead, register the hand-written Mongo stores directly as the interfaces
            // UserManager<ApplicationUser>/RoleManager<ApplicationRole> ask for.
            .AddDefaultTokenProviders();

        services.AddScoped<IUserStore<ApplicationUser>, MongoUserStore>();
        services.AddScoped<IRoleStore<ApplicationRole>, MongoRoleStore>();

        services.AddHttpContextAccessor();

        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IGoogleAuthVerifier, GoogleAuthVerifier>();

        services.AddHttpClient<IEmailService, ResendEmailService>();
        services.AddScoped<IImageStorageService, CloudinaryImageStorageService>();
        services.AddScoped<IPaymentService, StripePaymentService>();

        services.AddHealthChecks().AddCheck<MongoHealthCheck>("database");

        return services;
    }
}
