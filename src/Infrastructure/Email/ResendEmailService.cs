using System.Net.Http.Headers;
using System.Net.Http.Json;
using ECommerce.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ECommerce.Infrastructure.Email;

/// <summary>
/// Sends transactional email via Resend's REST API (https://resend.com/docs/api-reference/emails/send-email)
/// instead of SMTP. Resend only needs a single POST with an API key — no SMTP host/port/credentials,
/// no MailKit/MimeKit dependency, and it works the same whether running locally or deployed anywhere
/// that can reach the public internet (SMTP ports are frequently blocked by hosting providers; a plain
/// HTTPS POST almost never is).
/// </summary>
public class ResendEmailService : IEmailService
{
    private readonly HttpClient _httpClient;
    private readonly string _fromAddress;
    private readonly string _fromName;
    private readonly ILogger<ResendEmailService> _logger;

    public ResendEmailService(HttpClient httpClient, IConfiguration configuration, ILogger<ResendEmailService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var apiKey = configuration["Resend:ApiKey"]
            ?? throw new InvalidOperationException("Resend:ApiKey is not configured.");

        _httpClient.BaseAddress = new Uri("https://api.resend.com/");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        _fromAddress = configuration["Resend:FromAddress"] ?? "no-reply@example.com";
        _fromName = configuration["Resend:FromName"] ?? "Nova Marketplace";
    }

    public Task SendWelcomeEmailAsync(string toEmail, string firstName, CancellationToken cancellationToken = default) =>
        SendAsync(toEmail, "Welcome to Nova",
            $"<h2>Welcome, {firstName}!</h2><p>Thanks for creating an account. Start exploring today's deals and new arrivals.</p>",
            cancellationToken);

    public Task SendEmailVerificationAsync(string toEmail, string verificationLink, CancellationToken cancellationToken = default) =>
        SendAsync(toEmail, "Verify your email address",
            $"<p>Please confirm your email by clicking the link below:</p><p><a href=\"{verificationLink}\">Verify email</a></p>",
            cancellationToken);

    public Task SendPasswordResetAsync(string toEmail, string resetLink, CancellationToken cancellationToken = default) =>
        SendAsync(toEmail, "Reset your password",
            $"<p>We received a request to reset your password. This link expires shortly.</p><p><a href=\"{resetLink}\">Reset password</a></p>",
            cancellationToken);

    public Task SendOrderConfirmationAsync(string toEmail, string orderNumber, decimal total, CancellationToken cancellationToken = default) =>
        SendAsync(toEmail, $"Order confirmed — {orderNumber}",
            $"<p>Your order <strong>{orderNumber}</strong> has been placed. Total: <strong>₱{total:0.00}</strong>.</p>",
            cancellationToken);

    public Task SendPaymentConfirmationAsync(string toEmail, string orderNumber, decimal amount, CancellationToken cancellationToken = default) =>
        SendAsync(toEmail, $"Payment received — {orderNumber}",
            $"<p>We've received your payment of <strong>₱{amount:0.00}</strong> for order <strong>{orderNumber}</strong>.</p>",
            cancellationToken);

    private async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken)
    {
        var payload = new
        {
            from = $"{_fromName} <{_fromAddress}>",
            to = new[] { toEmail },
            subject,
            html = htmlBody
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync("emails", payload, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError(
                    "Resend API returned {StatusCode} sending to {ToEmail} with subject {Subject}: {Body}",
                    response.StatusCode, toEmail, subject, body);
            }
        }
        catch (Exception ex)
        {
            // Email delivery failures should never break the primary operation (registration, checkout, etc.)
            _logger.LogError(ex, "Failed to send email via Resend to {ToEmail} with subject {Subject}", toEmail, subject);
        }
    }
}
