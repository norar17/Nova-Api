using Google.Apis.Auth;
using Microsoft.Extensions.Configuration;

namespace ECommerce.Infrastructure.Auth;

public interface IGoogleAuthVerifier
{
    Task<GoogleProfile?> VerifyAsync(string idToken, CancellationToken cancellationToken = default);
}

public record GoogleProfile(string GoogleId, string Email, string FirstName, string LastName, string? AvatarUrl);

public class GoogleAuthVerifier : IGoogleAuthVerifier
{
    private readonly string _clientId;

    public GoogleAuthVerifier(IConfiguration configuration)
    {
        _clientId = configuration["Authentication:Google:ClientId"]
            ?? throw new InvalidOperationException("Authentication:Google:ClientId is not configured.");
    }

    public async Task<GoogleProfile?> VerifyAsync(string idToken, CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { _clientId }
            };

            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);

            return new GoogleProfile(
                payload.Subject,
                payload.Email,
                payload.GivenName ?? string.Empty,
                payload.FamilyName ?? string.Empty,
                payload.Picture);
        }
        catch (InvalidJwtException)
        {
            return null;
        }
    }
}
