using System.Globalization;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using RealKevbase.Core.Models;

using Stripe;
using Stripe.Checkout;

namespace RealKevbase.Core.Services;

/// <summary>
/// Stripe configuration, bound from the <c>Stripe</c> configuration section.
/// </summary>
/// <remarks>
/// <see cref="SecretKey"/> must never be stored in source control. Supply it through an
/// environment variable (<c>Stripe__SecretKey</c>), user secrets or a key vault.
/// </remarks>
public sealed class StripeOptions
{
    /// <summary>
    /// The configuration section these options are read from.
    /// </summary>
    public const string SectionName = "Stripe";

    /// <summary>
    /// Gets or sets the Stripe secret API key. Required; never committed to source.
    /// </summary>
    public string? SecretKey { get; set; }

    /// <summary>
    /// Gets or sets an optional Stripe API base URL override, used to point at a local
    /// Stripe-compatible mock in tests. Leave empty in production.
    /// </summary>
    public string? ApiBase { get; set; }

    /// <summary>
    /// Gets or sets the absolute URL Stripe redirects to after a successful payment.
    /// The <c>{CHECKOUT_SESSION_ID}</c> placeholder is appended when absent.
    /// </summary>
    public string? SuccessUrl { get; set; }

    /// <summary>
    /// Gets or sets the absolute URL Stripe redirects to when the buyer abandons checkout.
    /// </summary>
    public string? CancelUrl { get; set; }

    /// <summary>
    /// Gets or sets the endpoint secret used to verify Stripe webhook signatures.
    /// Required by <c>POST /api/checkout/webhook</c>; never committed to source.
    /// </summary>
    public string? WebhookSecret { get; set; }

    /// <summary>
    /// Gets or sets the maximum accepted age of a webhook signature, in seconds.
    /// Defaults to 300 seconds, matching Stripe's recommendation.
    /// </summary>
    public long WebhookToleranceSeconds { get; set; } = 300;

    /// <summary>
    /// Gets or sets the ISO 4217 currency code used for every line item. Defaults to <c>usd</c>.
    /// </summary>
    public string Currency { get; set; } = "usd";

    /// <summary>
    /// Gets or sets the ISO 3166-1 alpha-2 countries checkout may ship merchandise to.
    /// Required whenever the cart contains physical merchandise.
    /// </summary>
    public string[] ShippingCountries { get; set; } = [];

    /// <summary>
    /// Gets or sets an optional flat shipping rate in minor units (for example 500 = $5.00).
    /// When null or zero, no shipping charge is added and the address is only collected.
    /// </summary>
    public long? ShippingRateAmountCents { get; set; }

    /// <summary>
    /// Gets or sets the label shown for the shipping rate at checkout.
    /// </summary>
    public string ShippingRateDisplayName { get; set; } = "Standard shipping";
}

/// <summary>
/// Creates Stripe Checkout Sessions from validated catalog lines.
/// </summary>
/// <remarks>
/// This service only creates the payment session. Payment confirmation and fulfillment
/// (issuing PDF download links) happen in the Stripe webhook, asynchronously, so a slow
/// buyer or an unavailable email provider can never block this HTTP request.
/// </remarks>
public sealed class StripeCheckoutService : IStripeCheckoutService
{
    private const string SessionIdPlaceholder = "{CHECKOUT_SESSION_ID}";

    // Stripe metadata values are limited to 500 characters.
    private const int MaxMetadataValueLength = 480;

    private readonly StripeClient _client;
    private readonly StripeOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="StripeCheckoutService"/> class.
    /// </summary>
    /// <param name="client">Configured Stripe API client.</param>
    /// <param name="options">Checkout configuration.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null"/>.</exception>
    public StripeCheckoutService(StripeClient client, StripeOptions options)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);

        _client = client;
        _options = options;
    }

    /// <inheritdoc />
    public async Task<CheckoutSessionResult> CreateSessionAsync(
        IReadOnlyCollection<CheckoutLine> lines,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (lines.Count == 0)
        {
            throw new ArgumentException("At least one checkout line is required.", nameof(lines));
        }

        var currency = Require(_options.Currency, nameof(StripeOptions.Currency));
        var successUrl = BuildSuccessUrl(Require(_options.SuccessUrl, nameof(StripeOptions.SuccessUrl)));
        var cancelUrl = Require(_options.CancelUrl, nameof(StripeOptions.CancelUrl));

        var requiresShipping = false;
        foreach (var line in lines)
        {
            if (line.Quantity < 1)
            {
                throw new ArgumentException(
                    $"Quantity for product '{line.Product.Id}' must be at least 1.",
                    nameof(lines));
            }

            line.Product.Validate();
            requiresShipping |= line.Product.Fulfillment == FulfillmentType.PhysicalMerch;
        }

        var cartId = Guid.NewGuid().ToString("N");

        var sessionOptions = new SessionCreateOptions
        {
            Mode = "payment",
            Currency = currency,
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            ClientReferenceId = cartId,
            LineItems = lines.Select(line => CreateLineItem(line, currency)).ToList(),
            Metadata = CreateSessionMetadata(lines, cartId, requiresShipping),
            // One session handles mixed carts: shipping is collected once, and only
            // when the order actually contains something that has to be delivered.
            ShippingAddressCollection = requiresShipping ? CreateShippingAddressCollection() : null,
            ShippingOptions = requiresShipping ? CreateShippingOptions(currency) : null
        };

        var session = await _client.V1.Checkout.Sessions
            .CreateAsync(sessionOptions, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return new CheckoutSessionResult(session.Id, session.Url);
    }

    /// <summary>
    /// Builds one Stripe line item. Prices always come from the catalog product, never from
    /// the browser, and are converted to the currency's minor unit.
    /// </summary>
    private static SessionLineItemOptions CreateLineItem(CheckoutLine line, string currency)
    {
        var product = line.Product;

        return new SessionLineItemOptions
        {
            Quantity = line.Quantity,
            PriceData = new SessionLineItemPriceDataOptions
            {
                Currency = currency,
                UnitAmount = ToMinorUnits(product.Price),
                ProductData = new SessionLineItemPriceDataProductDataOptions
                {
                    Name = product.Name,
                    Description = product.IsDigitalPdf
                        ? "PDF download — delivered instantly after payment"
                        : "Physical item — ships in 2–4 business days",
                },
            },
            // Surfaced on the session's line items so eventual webhook fulfillment can
            // resolve the product without trusting client input.
            Metadata = new Dictionary<string, string>
            {
                ["product_id"] = product.Id.ToString(),
                ["fulfillment"] = product.Fulfillment.ToString(),
            },
        };
    }

    /// <summary>
    /// Session metadata used by the asynchronous webhook to correlate and route the order.
    /// </summary>
    private static Dictionary<string, string> CreateSessionMetadata(
        IReadOnlyCollection<CheckoutLine> lines,
        string cartId,
        bool containsPhysicalMerch)
    {
        var productIds = string.Join(
            ',',
            lines.Select(line => line.Product.Id.ToString()).Distinct(StringComparer.Ordinal));

        if (productIds.Length > MaxMetadataValueLength)
        {
            productIds = productIds[..MaxMetadataValueLength];
        }

        return new Dictionary<string, string>
        {
            ["cart_id"] = cartId,
            ["contains_physical_merch"] = containsPhysicalMerch ? "true" : "false",
            ["line_count"] = lines.Count.ToString(CultureInfo.InvariantCulture),
            ["product_ids"] = productIds,
        };
    }

    /// <summary>
    /// Collects a shipping address for merchandise orders, limited to the configured countries.
    /// </summary>
    private SessionShippingAddressCollectionOptions CreateShippingAddressCollection()
    {
        if (_options.ShippingCountries.Length == 0)
        {
            throw new InvalidOperationException(
                $"'{StripeOptions.SectionName}:{nameof(StripeOptions.ShippingCountries)}' must list at least one country when the cart contains physical merchandise.");
        }

        return new SessionShippingAddressCollectionOptions
        {
            AllowedCountries = _options.ShippingCountries.ToList(),
        };
    }

    /// <summary>
    /// Adds an optional flat shipping rate when one is configured; otherwise shipping is free
    /// and only the address is collected.
    /// </summary>
    private List<SessionShippingOptionOptions>? CreateShippingOptions(string currency)
    {
        if (_options.ShippingRateAmountCents is not > 0)
        {
            return null;
        }

        var displayName = string.IsNullOrWhiteSpace(_options.ShippingRateDisplayName)
            ? "Standard shipping"
            : _options.ShippingRateDisplayName;

        return
        [
            new SessionShippingOptionOptions
            {
                ShippingRateData = new SessionShippingOptionShippingRateDataOptions
                {
                    Type = "fixed_amount",
                    DisplayName = displayName,
                    FixedAmount = new SessionShippingOptionShippingRateDataFixedAmountOptions
                    {
                        Amount = _options.ShippingRateAmountCents,
                        Currency = currency,
                    },
                },
            },
        ];
    }

    /// <summary>
    /// Ensures Stripe can substitute the session id into the success URL.
    /// </summary>
    private static string BuildSuccessUrl(string configuredUrl)
    {
        if (configuredUrl.Contains(SessionIdPlaceholder, StringComparison.Ordinal))
        {
            return configuredUrl;
        }

        var separator = configuredUrl.Contains('?') ? '&' : '?';
        return $"{configuredUrl}{separator}session_id={SessionIdPlaceholder}";
    }

    /// <summary>
    /// Converts a major-unit price to the currency's minor unit (cents for USD).
    /// </summary>
    private static long ToMinorUnits(decimal amount)
    {
        return checked((long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero));
    }

    /// <summary>
    /// Reads a required configuration value, failing with an actionable message.
    /// </summary>
    private static string Require(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Stripe configuration '{StripeOptions.SectionName}:{name}' is required.");
        }

        return value;
    }
}

/// <summary>
/// Registration helpers for Stripe Checkout.
/// </summary>
public static class StripeCheckoutServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="StripeCheckoutService"/> as the <see cref="IStripeCheckoutService"/>
    /// using the <c>Stripe</c> configuration section.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">Application configuration containing the Stripe settings.</param>
    /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null"/>.</exception>
    public static IServiceCollection AddStripeCheckout(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(StripeOptions.SectionName);

        var options = new StripeOptions
        {
            SecretKey = section[nameof(StripeOptions.SecretKey)],
            ApiBase = section[nameof(StripeOptions.ApiBase)],
            WebhookSecret = section[nameof(StripeOptions.WebhookSecret)],
            WebhookToleranceSeconds = long.TryParse(
                section[nameof(StripeOptions.WebhookToleranceSeconds)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var webhookTolerance) && webhookTolerance > 0
                ? webhookTolerance
                : 300,
            SuccessUrl = section[nameof(StripeOptions.SuccessUrl)],
            CancelUrl = section[nameof(StripeOptions.CancelUrl)],
            Currency = section[nameof(StripeOptions.Currency)] is { Length: > 0 } currency ? currency : "usd",
            ShippingCountries = section.GetSection(nameof(StripeOptions.ShippingCountries))
                .GetChildren()
                .Select(child => child.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToArray(),
            ShippingRateAmountCents = long.TryParse(
                section[nameof(StripeOptions.ShippingRateAmountCents)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var cents) && cents > 0
                ? cents
                : null,
            ShippingRateDisplayName = section[nameof(StripeOptions.ShippingRateDisplayName)] is { Length: > 0 } label
                ? label
                : "Standard shipping",
        };

        services.AddSingleton(options);
        services.AddSingleton(_ => CreateStripeClient(options));
        services.AddSingleton<IStripeCheckoutService, StripeCheckoutService>();

        return services;
    }

    private static StripeClient CreateStripeClient(StripeOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SecretKey))
        {
            throw new InvalidOperationException(
                $"Stripe is not configured. Set '{StripeOptions.SectionName}:{nameof(StripeOptions.SecretKey)}' " +
                "through configuration, environment variables or user secrets.");
        }

        return string.IsNullOrWhiteSpace(options.ApiBase)
            ? new StripeClient(options.SecretKey)
            : new StripeClient(new StripeClientOptions
            {
                ApiKey = options.SecretKey,
                ApiBase = options.ApiBase,
            });
    }
}
