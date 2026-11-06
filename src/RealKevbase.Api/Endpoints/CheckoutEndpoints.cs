using Microsoft.Extensions.Logging;

using RealKevbase.Core.Models;
using RealKevbase.Core.Services;

using Stripe;

namespace RealKevbase.Api.Endpoints;

/// <summary>
/// Checkout endpoints, grouped under <c>/api/checkout</c>.
/// </summary>
public static class CheckoutEndpoints
{
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
