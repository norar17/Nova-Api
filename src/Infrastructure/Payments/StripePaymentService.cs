using ECommerce.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Stripe;

namespace ECommerce.Infrastructure.Payments;

public class StripePaymentService : IPaymentService
{
    private readonly string _webhookSecret;
    private readonly ILogger<StripePaymentService> _logger;
    private readonly bool _isDevelopment;

    public StripePaymentService(IConfiguration configuration, ILogger<StripePaymentService> logger, IHostEnvironment environment)
    {
        _logger = logger;
        _isDevelopment = environment.IsDevelopment();

        StripeConfiguration.ApiKey = configuration["Stripe:SecretKey"]
            ?? throw new InvalidOperationException("Stripe:SecretKey is not configured.");
        _webhookSecret = configuration["Stripe:WebhookSecret"]
            ?? throw new InvalidOperationException("Stripe:WebhookSecret is not configured.");
    }

    public async Task<(string ClientSecret, string PaymentIntentId)> CreatePaymentIntentAsync(
        decimal amount, string currency, Guid orderId, CancellationToken cancellationToken = default)
    {
        var service = new PaymentIntentService();

        var options = new PaymentIntentCreateOptions
        {
            Amount = (long)(amount * 100), // Stripe expects the smallest currency unit
            Currency = currency,
            Metadata = new Dictionary<string, string> { { "orderId", orderId.ToString() } },
            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions { Enabled = true }
        };

        var intent = await service.CreateAsync(options, cancellationToken: cancellationToken);
        return (intent.ClientSecret, intent.Id);
    }

    public Task<bool> ConfirmWebhookSignatureAsync(string payload, string signatureHeader)
    {
        try
        {
            EventUtility.ConstructEvent(payload, signatureHeader, _webhookSecret);
            return Task.FromResult(true);
        }
        catch (StripeException ex)
        {
            if (_isDevelopment)
            {
                // Local dev is the one place where signature mismatches are expected and harmless:
                // `stripe listen` issues a brand-new webhook secret every time it's restarted, so it
                // drifts from appsettings.Local.json constantly, and there's no real attacker who can
                // reach a machine's localhost anyway. Loudly accept anyway so the rest of the
                // checkout flow (order status, stock, cart) can actually be tested. Production keeps
                // the strict check below — this branch never runs outside Development.
                _logger.LogWarning(ex,
                    "Stripe webhook signature check FAILED but was accepted anyway because ASPNETCORE_ENVIRONMENT=Development. " +
                    "Reason: {Reason}. Configured secret starts with {SecretPrefix}... — if this keeps happening, copy the " +
                    "*current* 'whsec_...' shown by your running `stripe listen` command into appsettings.Local.json and restart the API. " +
                    "This bypass does NOT apply outside Development.",
                    ex.Message, _webhookSecret.Length > 10 ? _webhookSecret[..10] : _webhookSecret);
                return Task.FromResult(true);
            }

            _logger.LogWarning(ex, "Stripe webhook signature check failed: {Reason}", ex.Message);
            return Task.FromResult(false);
        }
    }
}
