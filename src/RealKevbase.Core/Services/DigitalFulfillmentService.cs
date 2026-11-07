using Microsoft.Extensions.Logging;

using RealKevbase.Core.Models;

using CatalogProduct = RealKevbase.Core.Models.Product;

using Stripe;
using Stripe.Checkout;

namespace RealKevbase.Core.Services;

/// <summary>
/// Default <see cref="IDigitalFulfillmentService"/>: re-reads the paid session from Stripe
/// (so line items cannot be spoofed), resolves the digital products through the catalog and
/// issues 24-hour pre-signed download links.
/// </summary>
public sealed class DigitalFulfillmentService : IDigitalFulfillmentService
{
    /// <summary>
    /// Line-item metadata key written when the checkout session is created; links a line
    /// back to the catalog product that priced it.
    /// </summary>
    public const string ProductIdMetadataKey = "product_id";

    private const string PaidStatus = "paid";

    private readonly StripeClient _stripeClient;
    private readonly IProductCatalog _catalog;
    private readonly IPdfStorageService _pdfStorage;
    private readonly IPdfDeliveryService _delivery;
    private readonly ILogger<DigitalFulfillmentService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DigitalFulfillmentService"/> class.
    /// </summary>
    /// <param name="stripeClient">Stripe client used to re-read the session.</param>
    /// <param name="catalog">Catalog used to resolve products and fulfillment types.</param>
    /// <param name="pdfStorage">Storage used to sign download links.</param>
    /// <param name="delivery">Delivery channel for the issued links.</param>
    /// <param name="logger">Logger for fulfillment outcomes.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null"/>.</exception>
    public DigitalFulfillmentService(
        StripeClient stripeClient,
        IProductCatalog catalog,
        IPdfStorageService pdfStorage,
        IPdfDeliveryService delivery,
        ILogger<DigitalFulfillmentService> logger)
    {
        ArgumentNullException.ThrowIfNull(stripeClient);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(pdfStorage);
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(logger);

        _stripeClient = stripeClient;
        _catalog = catalog;
        _pdfStorage = pdfStorage;
        _delivery = delivery;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task FulfillAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var delivery = await IssueAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (delivery is null)
        {
            // Unpaid sessions are logged by IssueAsync; nothing to hand off.
            return;
        }

        if (delivery.Downloads.Count == 0)
        {
            _logger.LogInformation(
                "Session {SessionId} contains no digital products; nothing to deliver.",
                delivery.SessionId);
            return;
        }

        await _delivery.DeliverAsync(delivery, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Fulfilled {ProductCount} digital product(s) for Stripe session {SessionId}.",
            delivery.Downloads.Count,
            delivery.SessionId);
    }

    /// <inheritdoc />
    public async Task<PdfDelivery?> IssueAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        // Expand line items so fulfillment works from authoritative Stripe data rather than
        // from metadata supplied by the browser. This is also what makes the success-page
        // downloads endpoint safe: an unpaid or unknown session never yields a link.
        var session = await _stripeClient.V1.Checkout.Sessions
            .GetAsync(
                sessionId,
                new SessionGetOptions { Expand = ["line_items"] },
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!string.Equals(session.PaymentStatus, PaidStatus, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Session {SessionId} is not paid (payment status '{PaymentStatus}'); no downloads are available.",
                session.Id,
                session.PaymentStatus);
            return null;
        }

        var digitalProducts = ResolveDigitalProducts(session);
        var downloads = new List<PdfDownload>(digitalProducts.Count);

        foreach (var product in digitalProducts.Values)
        {
            var url = await _pdfStorage.CreateDownloadUrlAsync(product, cancellationToken).ConfigureAwait(false);
            downloads.Add(new PdfDownload(product, url, DateTimeOffset.UtcNow.Add(_pdfStorage.LinkLifetime)));
        }

        var customerEmail = session.CustomerDetails?.Email ?? session.CustomerEmail;
        return new PdfDelivery(session.Id, customerEmail, downloads);
    }

    /// <summary>
    /// Maps the session's line items onto catalog products, keeping only digital PDFs.
    /// Unknown or non-digital lines (merch) are ignored.
    /// </summary>
    private Dictionary<Guid, CatalogProduct> ResolveDigitalProducts(Session session)
    {
        var products = new Dictionary<Guid, CatalogProduct>();

        foreach (var lineItem in session.LineItems?.Data ?? [])
        {
            if (lineItem.Metadata is null ||
                !lineItem.Metadata.TryGetValue(ProductIdMetadataKey, out var rawProductId) ||
                !Guid.TryParse(rawProductId, out var productId))
            {
                continue;
            }

            if (!_catalog.TryGetProduct(productId, out var product) || !product.IsDigitalPdf)
            {
                continue;
            }

            // One link per product, even if the same product appears on several lines.
            products[productId] = product;
        }

        return products;
    }
}
