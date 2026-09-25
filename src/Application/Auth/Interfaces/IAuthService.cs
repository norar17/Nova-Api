using ECommerce.Application.Auth.DTOs;
using ECommerce.Shared;

namespace ECommerce.Application.Auth.Interfaces;

public interface IAuthService
{
    Task<Result> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task<Result<AuthResponse>> LoginAsync(LoginRequest request, string ipAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds an existing user by Google subject id / email, or provisions a new one, then issues tokens.
    /// The caller (API layer, via IGoogleAuthVerifier in Infrastructure) is responsible for verifying the
    /// raw Google ID token first — this method trusts the profile fields it is given.
    /// </summary>
    Task<Result<AuthResponse>> FindOrCreateFromGoogleAsync(
        string googleId, string email, string firstName, string lastName, string? avatarUrl,
        string ipAddress, CancellationToken cancellationToken = default);
    Task<Result<AuthResponse>> RefreshTokenAsync(string rawRefreshToken, string ipAddress, CancellationToken cancellationToken = default);
    Task<Result> RevokeTokenAsync(string rawRefreshToken, string ipAddress, CancellationToken cancellationToken = default);
    Task<Result> RevokeAllTokensAsync(Guid userId, string ipAddress, CancellationToken cancellationToken = default);
    Task<Result> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default);
    Task<Result> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);
    Task<Result> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default);
    Task<Result> VerifyEmailAsync(VerifyEmailRequest request, CancellationToken cancellationToken = default);
    Task<Result> ResendVerificationEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<Result> DeleteAccountAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Result<UserProfileDto>> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Result<UserProfileDto>> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken = default);
    Task<Result<UserProfileDto>> UpdateAvatarAsync(Guid userId, Stream fileStream, string fileName, CancellationToken cancellationToken = default);
}
