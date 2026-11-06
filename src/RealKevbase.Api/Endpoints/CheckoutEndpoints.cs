using RealKevbase.Core.Models;
using RealKevbase.Core.Services;

namespace RealKevbase.Api.Endpoints;

/// <summary>
/// Checkout endpoints, grouped under <c>/api/checkout</c>.
/// </summary>
public static class CheckoutEndpoints
{
    // Placeholder catalog so the download endpoint is runnable today. Replace with the
    // persistence-backed product repository once the catalog layer exists.
    private static readonly IReadOnlyDictionary<Guid, Product> Catalog = new Dictionary<Guid, Product>
    {
        [Guid.Parse("6f1b6a9e-2f4c-4f1e-9a1d-0c2b3e4d5f60")] = new Product
        {
            Id = Guid.Parse("6f1b6a9e-2f4c-4f1e-9a1d-0c2b3e4d5f60"),
            Name = "RealKevbase Field Guide (PDF)",
            Price = 19.99m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/field-guide.pdf"
        },
        [Guid.Parse("9c2d4e6f-8a0b-4c1d-9e3f-5a7b9c1d3e5f")] = new Product
        {
            Id = Guid.Parse("9c2d4e6f-8a0b-4c1d-9e3f-5a7b9c1d3e5f"),
            Name = "RealKevbase Logo Tee",
            Price = 24.00m,
            Fulfillment = FulfillmentType.PhysicalMerch
        }
    };

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

        group.MapPost("/downloads/pdf", CreatePdfDownloadLinkAsync)
            .WithName("CreatePdfDownloadLink")
            .Produces<PdfDownloadLinkResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> CreatePdfDownloadLinkAsync(
        PdfDownloadRequest request,
        IPdfStorageService pdfStorage,
        CancellationToken cancellationToken)
    {
        if (!Catalog.TryGetValue(request.ProductId, out var product))
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
