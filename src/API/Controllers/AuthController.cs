using System.Security.Claims;
using ECommerce.Application.Auth.DTOs;
using ECommerce.Application.Auth.Interfaces;
using ECommerce.Infrastructure.Auth;
using ECommerce.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ECommerce.API.Controllers;

/// <summary>
/// Handles registration, login, token refresh, and account recovery.
/// Controllers only translate HTTP requests/responses to DTOs — all business logic lives in IAuthService.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
[EnableRateLimiting("auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IGoogleAuthVerifier _googleAuthVerifier;

    public AuthController(IAuthService authService, IGoogleAuthVerifier googleAuthVerifier)
    {
        _authService = authService;
        _googleAuthVerifier = googleAuthVerifier;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.RegisterAsync(request, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<object>.Ok(new { }, "Account created. Check your email for a verification link before signing in."))
            : BadRequest(ApiResponse<object>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpPost("resend-verification")]
    public async Task<IActionResult> ResendVerification([FromBody] ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        // Reuses ForgotPasswordRequest's shape (just an email) rather than adding a near-identical DTO.
        await _authService.ResendVerificationEmailAsync(request.Email, cancellationToken);
        return Ok(ApiResponse<object>.Ok(new { }, "If that email is registered and unverified, a new link has been sent."));
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var result = await _authService.LoginAsync(request, ipAddress, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<AuthResponse>.Ok(result.Value))
            : Unauthorized(ApiResponse<AuthResponse>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpPost("google")]
    public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginRequest request, CancellationToken cancellationToken)
    {
        var profile = await _googleAuthVerifier.VerifyAsync(request.IdToken, cancellationToken);
        if (profile is null)
        {
            return Unauthorized(ApiResponse<AuthResponse>.Fail("Invalid Google credential.", "INVALID_GOOGLE_TOKEN"));
        }

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var result = await _authService.FindOrCreateFromGoogleAsync(
            profile.GoogleId, profile.Email, profile.FirstName, profile.LastName, profile.AvatarUrl,
            ipAddress, cancellationToken);

        return result.IsSuccess
            ? Ok(ApiResponse<AuthResponse>.Ok(result.Value))
            : BadRequest(ApiResponse<AuthResponse>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var result = await _authService.RefreshTokenAsync(request.RefreshToken, ipAddress, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<AuthResponse>.Ok(result.Value))
            : Unauthorized(ApiResponse<AuthResponse>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var result = await _authService.RevokeTokenAsync(request.RefreshToken, ipAddress, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<object>.Ok(new { }, "Logged out."))
            : BadRequest(ApiResponse<object>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpPost("logout-all")]
    [Authorize]
    public async Task<IActionResult> LogoutAllDevices(CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var result = await _authService.RevokeAllTokensAsync(userId, ipAddress, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<object>.Ok(new { }, "Logged out from all devices."))
            : BadRequest(ApiResponse<object>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        await _authService.ForgotPasswordAsync(request, cancellationToken);
        // Always return a generic success message — never reveal whether the email exists
        return Ok(ApiResponse<object>.Ok(new { }, "If that email exists, a reset link has been sent."));
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.ResetPasswordAsync(request, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<object>.Ok(new { }, "Password reset successfully."))
            : BadRequest(ApiResponse<object>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _authService.ChangePasswordAsync(userId, request, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<object>.Ok(new { }, "Password changed successfully."))
            : BadRequest(ApiResponse<object>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.VerifyEmailAsync(request, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<object>.Ok(new { }, "Email verified successfully."))
            : BadRequest(ApiResponse<object>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpDelete("account")]
    [Authorize]
    public async Task<IActionResult> DeleteAccount(CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _authService.DeleteAccountAsync(userId, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<object>.Ok(new { }, "Account deleted."))
            : BadRequest(ApiResponse<object>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _authService.GetProfileAsync(userId, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<UserProfileDto>.Ok(result.Value))
            : NotFound(ApiResponse<UserProfileDto>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpPut("profile")]
    [Authorize]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _authService.UpdateProfileAsync(userId, request, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<UserProfileDto>.Ok(result.Value, "Profile updated."))
            : BadRequest(ApiResponse<UserProfileDto>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpPost("avatar")]
    [Authorize]
    [RequestSizeLimit(5_000_000)]
    public async Task<IActionResult> UpdateAvatar(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            return BadRequest(ApiResponse<object>.Fail("No file was uploaded.", "EMPTY_FILE"));
        }

        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await using var stream = file.OpenReadStream();
        var result = await _authService.UpdateAvatarAsync(userId, stream, file.FileName, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<UserProfileDto>.Ok(result.Value, "Avatar updated."))
            : BadRequest(ApiResponse<UserProfileDto>.Fail(result.Error!, result.ErrorCode));
    }
}
