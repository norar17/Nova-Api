using ECommerce.Domain.Entities;

namespace ECommerce.Application.Common.Interfaces;

/// <summary>
/// Exposes the identity of the currently authenticated request, populated from the JWT claims
/// by an Infrastructure implementation so Application services never touch HttpContext directly.
/// </summary>
public interface ICurrentUserService
{
    Guid? UserId { get; }
    string? Email { get; }
    bool IsInRole(string role);
    string? IpAddress { get; }
}

/// <summary>
/// Issues and validates JWT access tokens and opaque refresh tokens.
/// </summary>
public interface ITokenService
{
    string GenerateAccessToken(ApplicationUser user, IList<string> roles);
    string GenerateRefreshToken();
    string HashToken(string rawToken);
}

public interface IEmailService
{
    Task SendWelcomeEmailAsync(string toEmail, string firstName, CancellationToken cancellationToken = default);
    Task SendEmailVerificationAsync(string toEmail, string verificationLink, CancellationToken cancellationToken = default);
    Task SendPasswordResetAsync(string toEmail, string resetLink, CancellationToken cancellationToken = default);
    Task SendOrderConfirmationAsync(string toEmail, string orderNumber, decimal total, CancellationToken cancellationToken = default);
    Task SendPaymentConfirmationAsync(string toEmail, string orderNumber, decimal amount, CancellationToken cancellationToken = default);
}

public interface IImageStorageService
{
    Task<(string Url, string PublicId)> UploadAsync(Stream fileStream, string fileName, string folder, CancellationToken cancellationToken = default);
    Task DeleteAsync(string publicId, CancellationToken cancellationToken = default);
}

public interface IPaymentService
{
    Task<(string ClientSecret, string PaymentIntentId)> CreatePaymentIntentAsync(decimal amount, string currency, Guid orderId, CancellationToken cancellationToken = default);
    Task<bool> ConfirmWebhookSignatureAsync(string payload, string signatureHeader);
}

/// <summary>
/// Records an admin/system action for the audit trail. Kept separate from AuditLog entity CRUD
/// so calling code doesn't need to know about repositories.
/// </summary>
public interface IAuditService
{
    Task LogAsync(string action, string entityName, string? entityId, object? oldValues, object? newValues, CancellationToken cancellationToken = default);
}
