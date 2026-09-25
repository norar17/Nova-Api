using ECommerce.Application.Auth.DTOs;
using ECommerce.Application.Auth.Interfaces;
using ECommerce.Application.Common.Interfaces;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Interfaces;
using ECommerce.Shared;
using Microsoft.AspNetCore.Identity;
using MongoDB.Driver.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ECommerce.Application.Auth.Services;

public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITokenService _tokenService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmailService _emailService;
    private readonly IImageStorageService _imageStorage;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;

    private const int RefreshTokenDays = 7;
    private const int RememberMeRefreshTokenDays = 30;
    private const int AccessTokenMinutes = 15;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        ITokenService tokenService,
        IUnitOfWork unitOfWork,
        IEmailService emailService,
        IImageStorageService imageStorage,
        IConfiguration configuration,
        ILogger<AuthService> logger)
    {
        _userManager = userManager;
        _tokenService = tokenService;
        _unitOfWork = unitOfWork;
        _emailService = emailService;
        _imageStorage = imageStorage;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<Result> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await _userManager.FindByEmailAsync(request.Email);

        if (existing is not null)
        {
            if (existing.EmailConfirmed)
            {
                return Result.Failure("An account with this email already exists.", "EMAIL_TAKEN");
            }

            // Unverified account re-registering: don't make them guess — just resend the link.
            await SendVerificationEmailAsync(existing, cancellationToken);
            return Result.Success();
        }

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        var createResult = await _userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            var message = string.Join(" ", createResult.Errors.Select(e => e.Description));
            return Result.Failure(message, "REGISTRATION_FAILED");
        }

        await _userManager.AddToRoleAsync(user, "Customer");
        await SendVerificationEmailAsync(user, cancellationToken);
        await _emailService.SendWelcomeEmailAsync(user.Email!, user.FirstName, cancellationToken);

        return Result.Success();
    }

    private async Task SendVerificationEmailAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var verificationToken = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        var verificationLink = BuildFrontendLink("verify-email", user.Id, verificationToken);
        await _emailService.SendEmailVerificationAsync(user.Email!, verificationLink, cancellationToken);
    }

    public async Task<Result> ResendVerificationEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByEmailAsync(email);

        // Always return success regardless of whether the account exists or is already verified —
        // otherwise this endpoint becomes a way to enumerate registered emails.
        if (user is not null && !user.EmailConfirmed)
        {
            await SendVerificationEmailAsync(user, cancellationToken);
        }

        return Result.Success();
    }

    public async Task<Result<AuthResponse>> LoginAsync(LoginRequest request, string ipAddress, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null || user.IsDeleted || !user.IsActive)
        {
            return Result.Failure<AuthResponse>("Invalid email or password.", "INVALID_CREDENTIALS");
        }

        var passwordValid = await _userManager.CheckPasswordAsync(user, request.Password);
        if (!passwordValid)
        {
            return Result.Failure<AuthResponse>("Invalid email or password.", "INVALID_CREDENTIALS");
        }

        if (_userManager.Options.SignIn.RequireConfirmedEmail && !user.EmailConfirmed)
        {
            return Result.Failure<AuthResponse>("Please verify your email before logging in.", "EMAIL_NOT_CONFIRMED");
        }

        user.LastLoginAtUtc = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        return await BuildAuthResponseAsync(user, ipAddress, request.RememberMe, cancellationToken);
    }

    public async Task<Result<AuthResponse>> FindOrCreateFromGoogleAsync(
        string googleId, string email, string firstName, string lastName, string? avatarUrl,
        string ipAddress, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.Users.FirstOrDefaultAsync(
            u => u.GoogleId == googleId || u.Email == email, cancellationToken);

        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FirstName = firstName,
                LastName = lastName,
                AvatarUrl = avatarUrl,
                GoogleId = googleId,
                IsExternalLogin = true,
                IsActive = true
            };

            var createResult = await _userManager.CreateAsync(user);
            if (!createResult.Succeeded)
            {
                return Result.Failure<AuthResponse>("Unable to create account from Google profile.", "GOOGLE_SIGNUP_FAILED");
            }

            await _userManager.AddToRoleAsync(user, "Customer");
        }
        else if (user.GoogleId is null)
        {
            user.GoogleId = googleId;
            user.IsExternalLogin = true;
            await _userManager.UpdateAsync(user);
        }

        user.LastLoginAtUtc = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        return await BuildAuthResponseAsync(user, ipAddress, rememberMe: true, cancellationToken);
    }

    public async Task<Result<AuthResponse>> RefreshTokenAsync(string rawRefreshToken, string ipAddress, CancellationToken cancellationToken = default)
    {
        var tokenHash = _tokenService.HashToken(rawRefreshToken);
        var repo = _unitOfWork.Repository<RefreshToken>();

        var storedToken = await repo.Query()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

        if (storedToken is null || !storedToken.IsActive)
        {
            return Result.Failure<AuthResponse>("Invalid or expired refresh token.", "INVALID_REFRESH_TOKEN");
        }

        var user = await _userManager.FindByIdAsync(storedToken.UserId.ToString());
        if (user is null || user.IsDeleted || !user.IsActive)
        {
            return Result.Failure<AuthResponse>("Account is no longer active.", "ACCOUNT_INACTIVE");
        }

        // Rotate: revoke the presented token and issue a brand new one
        storedToken.RevokedAtUtc = DateTime.UtcNow;
        storedToken.RevokedByIp = ipAddress;
        repo.Update(storedToken);

        var response = await BuildAuthResponseAsync(user, ipAddress, rememberMe: false, cancellationToken);

        storedToken.ReplacedByTokenHash = _tokenService.HashToken(response.Value.RefreshToken);
        repo.Update(storedToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return response;
    }

    public async Task<Result> RevokeTokenAsync(string rawRefreshToken, string ipAddress, CancellationToken cancellationToken = default)
    {
        var tokenHash = _tokenService.HashToken(rawRefreshToken);
        var repo = _unitOfWork.Repository<RefreshToken>();

        var storedToken = await repo.Query()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

        if (storedToken is null || !storedToken.IsActive)
        {
            return Result.Failure("Invalid or already revoked token.", "INVALID_REFRESH_TOKEN");
        }

        storedToken.RevokedAtUtc = DateTime.UtcNow;
        storedToken.RevokedByIp = ipAddress;
        repo.Update(storedToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result> RevokeAllTokensAsync(Guid userId, string ipAddress, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<RefreshToken>();
        var activeTokens = await repo.Query()
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null && t.ExpiresAtUtc > DateTime.UtcNow)
            .ToListAsync(cancellationToken);

        foreach (var token in activeTokens)
        {
            token.RevokedAtUtc = DateTime.UtcNow;
            token.RevokedByIp = ipAddress;
            repo.Update(token);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);

        // Always return success to avoid leaking which emails are registered
        if (user is null || user.IsDeleted)
        {
            return Result.Success();
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var resetLink = BuildResetPasswordLink(user.Email!, token);
        await _emailService.SendPasswordResetAsync(user.Email!, resetLink, cancellationToken);

        return Result.Success();
    }

    public async Task<Result> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return Result.Failure("Invalid request.", "INVALID_REQUEST");
        }

        var result = await _userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            var message = string.Join(" ", result.Errors.Select(e => e.Description));
            return Result.Failure(message, "RESET_FAILED");
        }

        await RevokeAllTokensAsync(user.Id, "0.0.0.0", cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Result.Failure("User not found.", "NOT_FOUND");
        }

        var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            var message = string.Join(" ", result.Errors.Select(e => e.Description));
            return Result.Failure(message, "CHANGE_PASSWORD_FAILED");
        }

        return Result.Success();
    }

    public async Task<Result> VerifyEmailAsync(VerifyEmailRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(request.UserId);
        if (user is null)
        {
            return Result.Failure("Invalid verification link.", "INVALID_REQUEST");
        }

        var result = await _userManager.ConfirmEmailAsync(user, request.Token);
        if (!result.Succeeded)
        {
            return Result.Failure("Invalid or expired verification link.", "VERIFICATION_FAILED");
        }

        return Result.Success();
    }

    public async Task<Result> DeleteAccountAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Result.Failure("User not found.", "NOT_FOUND");
        }

        user.IsDeleted = true;
        user.IsActive = false;
        user.DeletedAtUtc = DateTime.UtcNow;
        user.Email = $"deleted-{user.Id}@deactivated.local";
        user.UserName = user.Email;

        await _userManager.UpdateAsync(user);
        await RevokeAllTokensAsync(userId, "0.0.0.0", cancellationToken);

        return Result.Success();
    }

    public async Task<Result<UserProfileDto>> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Result.NotFound<UserProfileDto>("User", userId);
        }

        var roles = (await _userManager.GetRolesAsync(user)).ToList();
        return Result.Success(ToProfileDto(user, roles));
    }

    public async Task<Result<UserProfileDto>> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Result.NotFound<UserProfileDto>("User", userId);
        }

        user.FirstName = request.FirstName;
        user.LastName = request.LastName;
        await _userManager.UpdateAsync(user);

        var roles = (await _userManager.GetRolesAsync(user)).ToList();
        return Result.Success(ToProfileDto(user, roles));
    }

    public async Task<Result<UserProfileDto>> UpdateAvatarAsync(Guid userId, Stream fileStream, string fileName, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Result.NotFound<UserProfileDto>("User", userId);
        }

        if (user.AvatarPublicId is not null)
        {
            await _imageStorage.DeleteAsync(user.AvatarPublicId, cancellationToken);
        }

        var (url, publicId) = await _imageStorage.UploadAsync(fileStream, fileName, $"avatars/{userId}", cancellationToken);
        user.AvatarUrl = url;
        user.AvatarPublicId = publicId;
        await _userManager.UpdateAsync(user);

        var roles = (await _userManager.GetRolesAsync(user)).ToList();
        return Result.Success(ToProfileDto(user, roles));
    }

    private static UserProfileDto ToProfileDto(ApplicationUser user, IList<string> roles) => new(
        user.Id, user.Email!, user.FirstName, user.LastName, user.AvatarUrl,
        user.EmailConfirmed, roles.ToList(), user.CreatedAtUtc);

    private async Task<Result<AuthResponse>> BuildAuthResponseAsync(
        ApplicationUser user, string ipAddress, bool rememberMe, CancellationToken cancellationToken)
    {
        var roles = (await _userManager.GetRolesAsync(user)).ToList();
        var accessToken = _tokenService.GenerateAccessToken(user, roles);
        var rawRefreshToken = _tokenService.GenerateRefreshToken();

        var refreshTokenEntity = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = _tokenService.HashToken(rawRefreshToken),
            CreatedByIp = ipAddress,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(rememberMe ? RememberMeRefreshTokenDays : RefreshTokenDays)
        };

        await _unitOfWork.Repository<RefreshToken>().AddAsync(refreshTokenEntity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var response = new AuthResponse(
            user.Id,
            user.Email!,
            user.FirstName,
            user.LastName,
            user.AvatarUrl,
            roles,
            accessToken,
            rawRefreshToken,
            DateTime.UtcNow.AddMinutes(AccessTokenMinutes));

        return Result.Success(response);
    }

    private string BuildFrontendLink(string path, Guid userId, string token)
    {
        var baseUrl = _configuration["Frontend:BaseUrl"] ?? "http://localhost:5173";
        var encodedToken = Uri.EscapeDataString(token);
        return $"{baseUrl}/{path}?userId={userId}&token={encodedToken}";
    }

    /// <summary>
    /// Reset-password is keyed by email (see <see cref="ResetPasswordRequest"/>), not userId like
    /// verify-email is — a separate builder avoids silently sending the wrong identifier in the link.
    /// </summary>
    private string BuildResetPasswordLink(string email, string token)
    {
        var baseUrl = _configuration["Frontend:BaseUrl"] ?? "http://localhost:5173";
        var encodedEmail = Uri.EscapeDataString(email);
        var encodedToken = Uri.EscapeDataString(token);
        return $"{baseUrl}/reset-password?email={encodedEmail}&token={encodedToken}";
    }
}
