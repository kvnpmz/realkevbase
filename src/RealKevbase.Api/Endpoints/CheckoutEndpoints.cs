using System.Text;

using Microsoft.Extensions.Logging;

using RealKevbase.Api.Services;
using RealKevbase.Core.Models;
using RealKevbase.Core.Services;

using Stripe;
using Stripe.Checkout;

namespace RealKevbase.Api.Endpoints;

/// <summary>
/// Checkout endpoints, grouped under <c>/api/checkout</c>.
/// </summary>
public static class CheckoutEndpoints
{
    /// <summary>
    /// Stripe event emitted once a Checkout Session's payment has succeeded.
    /// </summary>
    private const string CheckoutSessionCompletedEvent = "checkout.session.completed";

    /// <summary>
    /// Maps the checkout route group onto the application.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder to extend.</param>
    /// <returns>The same <paramref name="endpoints"/> instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoints"/> is <see langword="null"/>.</exception>
    public static IEndpointRouteBuilder MapCheckoutEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/api/checkout").WithTags("Checkout");

        group.MapPost("/sessions", CreateCheckoutSessionAsync)
            .WithName("CreateCheckoutSession")
            .WithSummary("Creates a Stripe Checkout Session for a cart.")
            .Produces<CheckoutSessionResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        group.MapPost("/webhook", HandleStripeWebhookAsync)
            .WithName("StripeWebhook")
            .WithSummary("Verifies and queues Stripe payment events for asynchronous fulfillment.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/downloads/pdf", CreatePdfDownloadLinkAsync)
            .WithName("CreatePdfDownloadLink")
            .WithSummary("Creates a 24-hour pre-signed download link for a PDF product.")
            .Produces<PdfDownloadLinkResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    /// <summary>
    /// Validates the requested cart against the catalog and creates a Stripe Checkout
    /// Session covering both digital PDFs and physical merchandise.
    /// </summary>
    /// <remarks>
    /// Prices are always read from <see cref="IProductCatalog"/>; the request only carries
    /// product ids and quantities. Payment confirmation and PDF fulfillment are handled by
    /// the Stripe webhook asynchronously, never in this request.
    /// </remarks>
    private static async Task<IResult> CreateCheckoutSessionAsync(
        CheckoutSessionRequest request,
        IProductCatalog catalog,
        IStripeCheckoutService stripe,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (request.Items is not { Count: > 0 })
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Empty cart",
                detail: "At least one item is required to start checkout.");
        }

        // Aggregate duplicate lines so a client cannot split quantities to bypass checks.
        var requestedQuantities = new Dictionary<Guid, long>();
        foreach (var item in request.Items)
        {
            if (item.Quantity < 1)
            {
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid quantity",
                    detail: $"Quantity for product '{item.ProductId}' must be at least 1.");
            }

            requestedQuantities[item.ProductId] =
                requestedQuantities.GetValueOrDefault(item.ProductId) + item.Quantity;
        }

        var lines = new List<CheckoutLine>(requestedQuantities.Count);
        var unknownProductIds = new List<Guid>();

        foreach (var (productId, quantity) in requestedQuantities)
        {
            if (catalog.TryGetProduct(productId, out var product))
            {
                lines.Add(new CheckoutLine(product, quantity));
            }
            else
            {
                unknownProductIds.Add(productId);
            }
        }

        if (unknownProductIds.Count > 0)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Unknown products",
                detail: $"These products are not in the catalog: {string.Join(", ", unknownProductIds)}.");
        }

        try
        {
            var session = await stripe.CreateSessionAsync(lines, cancellationToken);
            return TypedResults.Ok(new CheckoutSessionResponse(session.SessionId, session.Url));
        }
        catch (StripeException exception)
        {
            loggerFactory
                .CreateLogger(typeof(CheckoutEndpoints))
                .LogError(exception, "Stripe rejected checkout session creation (code: {StripeCode}).", exception.StripeError?.Code);

            return TypedResults.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Checkout unavailable",
                detail: "We could not start checkout right now. Please try again in a moment.");
        }
        catch (HttpRequestException exception)
        {
            loggerFactory
                .CreateLogger(typeof(CheckoutEndpoints))
                .LogError(exception, "Could not reach Stripe to create a checkout session.");

            return TypedResults.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Checkout unavailable",
                detail: "We could not start checkout right now. Please try again in a moment.");
        }
    }

    /// <summary>
    /// Receives Stripe webhook events, verifies the signature and queues paid sessions for
    /// background fulfillment. The request does no link generation or emailing, so Stripe
    /// is acknowledged immediately.
    /// </summary>
    /// <remarks>
    /// Stripe retries any non-2xx response. A 400 means the signature did not verify; a 503
    /// means the in-process queue is saturated and the event should be retried.
    /// </remarks>
    private static async Task<IResult> HandleStripeWebhookAsync(
        HttpRequest request,
        StripeOptions stripeOptions,
        ICheckoutFulfillmentQueue fulfillmentQueue,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(typeof(CheckoutEndpoints));

        if (string.IsNullOrWhiteSpace(stripeOptions.WebhookSecret))
        {
            logger.LogError(
                "Stripe webhook received but '{Section}:{Setting}' is not configured.",
                StripeOptions.SectionName,
                nameof(StripeOptions.WebhookSecret));

            return TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Webhook not configured",
                detail: "Stripe webhook handling is not available.");
        }

        var signature = request.Headers["Stripe-Signature"].ToString();
        if (string.IsNullOrWhiteSpace(signature))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Missing signature",
                detail: "The Stripe-Signature header is required.");
        }

        string json;
        using (var reader = new StreamReader(request.Body, Encoding.UTF8))
        {
            json = await reader.ReadToEndAsync(cancellationToken);
        }

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(
                json,
                signature,
                stripeOptions.WebhookSecret,
                stripeOptions.WebhookToleranceSeconds,
                throwOnApiVersionMismatch: false);
        }
        catch (StripeException exception)
        {
            logger.LogWarning(exception, "Rejected a Stripe webhook whose signature did not verify.");
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid signature",
                detail: "The Stripe signature could not be verified.");
        }

        if (!string.Equals(stripeEvent.Type, CheckoutSessionCompletedEvent, StringComparison.Ordinal))
        {
            // Acknowledge unhandled event types so Stripe stops retrying them.
            logger.LogDebug("Ignoring Stripe event {EventId} of type '{EventType}'.", stripeEvent.Id, stripeEvent.Type);
            return TypedResults.Ok();
        }

        if (stripeEvent.Data.Object is not Session session)
        {
            logger.LogWarning("Stripe event {EventId} did not contain a Checkout Session.", stripeEvent.Id);
            return TypedResults.Ok();
        }

        if (!string.Equals(session.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase))
        {
            // Delayed payment methods complete the session before the money arrives; the
            // later checkout.session.async_payment_succeeded event will trigger fulfillment.
            logger.LogInformation(
                "Session {SessionId} completed with payment status '{PaymentStatus}'; fulfillment is deferred.",
                session.Id,
                session.PaymentStatus);

            return TypedResults.Ok();
        }

        if (!fulfillmentQueue.TryEnqueue(session.Id))
        {
            logger.LogWarning(
                "Fulfillment queue is full; returning 503 so Stripe retries session {SessionId}.",
                session.Id);

            return TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Fulfillment queue unavailable",
                detail: "The webhook could not be queued. Please retry.");
        }

        logger.LogInformation("Queued Stripe session {SessionId} for PDF fulfillment.", session.Id);
        return TypedResults.Ok();
    }

    /// <summary>
    /// Creates a read-only, 24-hour pre-signed download link for a digital product.
    /// </summary>
    private static async Task<IResult> CreatePdfDownloadLinkAsync(
        PdfDownloadRequest request,
        IProductCatalog catalog,
        IPdfStorageService pdfStorage,
        CancellationToken cancellationToken)
    {
        if (!catalog.TryGetProduct(request.ProductId, out var product))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Product not found",
                detail: $"No product with id '{request.ProductId}' exists.");
        }

        if (!product.IsDigitalPdf)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Product has no digital download",
                detail: $"Product '{product.Id}' is fulfilled as {product.Fulfillment} and has no PDF download.");
        }

        var downloadUrl = await pdfStorage.CreateDownloadUrlAsync(product, cancellationToken);

        return TypedResults.Ok(new PdfDownloadLinkResponse(
            product.Id,
            downloadUrl,
            DateTimeOffset.UtcNow.Add(pdfStorage.LinkLifetime)));
    }
}

/// <summary>
/// Request body for creating a Stripe Checkout Session.
/// </summary>
/// <param name="Items">Cart lines; only product ids and quantities are accepted.</param>
public sealed record CheckoutSessionRequest(IReadOnlyList<CheckoutSessionItem>? Items);

/// <summary>
/// A single requested cart line.
/// </summary>
/// <param name="ProductId">Identifier of a product in the catalog.</param>
/// <param name="Quantity">Positive quantity ordered.</param>
public sealed record CheckoutSessionItem(Guid ProductId, int Quantity);

/// <summary>
/// Stripe Checkout Session returned to the storefront.
/// </summary>
/// <param name="SessionId">Stripe Checkout Session identifier.</param>
/// <param name="Url">Stripe-hosted checkout URL to redirect the buyer to.</param>
public sealed record CheckoutSessionResponse(string SessionId, string? Url);

/// <summary>
/// Request body for creating a PDF download link.
/// </summary>
/// <param name="ProductId">Identifier of the digital PDF product to download.</param>
public sealed record PdfDownloadRequest(Guid ProductId);

/// <summary>
/// A pre-signed PDF download link.
/// </summary>
/// <param name="ProductId">Identifier of the product the link belongs to.</param>
/// <param name="DownloadUrl">Read-only Azure Blob Storage SAS URL.</param>
/// <param name="ExpiresAt">Moment the URL stops working (24 hours after it was issued).</param>
public sealed record PdfDownloadLinkResponse(Guid ProductId, Uri DownloadUrl, DateTimeOffset ExpiresAt);
