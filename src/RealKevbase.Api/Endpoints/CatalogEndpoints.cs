using RealKevbase.Api.Listings;

using RealKevbase.Core.Models;
using RealKevbase.Core.Services;

namespace RealKevbase.Api.Endpoints;

/// <summary>
/// Public catalog endpoints, grouped under <c>/api</c>.
/// </summary>
/// <remarks>
/// These endpoints are the only way the storefront learns what is for sale. They deliberately
/// never return <see cref="Product.PdfBlobName"/> or any other storage detail: a browser gets
/// product ids, prices and presentation data, and download URLs are minted separately, after a
/// paid Stripe session has been verified.
/// </remarks>
public static class CatalogEndpoints
{
    /// <summary>
    /// Maps the catalog route group onto the application.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder to extend.</param>
    /// <param name="requireAuthorization">
    /// When <see langword="true"/>, the catalog listing requires a bearer token from
    /// <c>POST /api/auth/token</c>. Thumbnails stay anonymous: they are loaded by
    /// <c>&lt;img&gt;</c> tags, which cannot carry an Authorization header.
    /// </param>
    /// <returns>The same <paramref name="endpoints"/> instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoints"/> is <see langword="null"/>.</exception>
    public static IEndpointRouteBuilder MapCatalogEndpoints(
        this IEndpointRouteBuilder endpoints,
        bool requireAuthorization = false)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/api").WithTags("Catalog");

        var products = group.MapGet("/products", GetProducts)
            .WithName("GetProducts")
            .WithSummary("Returns every purchasable product, without storage details.")
            .Produces<IReadOnlyList<ProductResponse>>();

        if (requireAuthorization)
        {
            products.RequireAuthorization();
        }

        group.MapGet("/products/{id:guid}/thumbnail", GetProductThumbnailAsync)
            .WithName("GetProductThumbnail")
            .WithSummary("Streams the artwork for a digital product.")
            .Produces(StatusCodes.Status200OK, contentType: "image/webp")
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    /// <summary>
    /// Returns the purchasable catalog.
    /// </summary>
    /// <remarks>
    /// A digital product is only offered when its listing exists, so the listing database stays
    /// the record of which PDFs have actually been produced. Physical merchandise has no listing
    /// and is always included. When the listing database cannot be read at all, every catalog
    /// product is returned: a missing file must not empty the storefront.
    /// </remarks>
    private static IResult GetProducts(IProductCatalog catalog, IListingRepository listings)
    {
        var available = listings.GetAll();
        var listingIds = available?.Select(listing => listing.Id).ToHashSet(StringComparer.Ordinal);

        var products = catalog.Products
            .Where(product => !product.IsDigitalPdf
                              || listingIds is null
                              || (product.ListingId is not null && listingIds.Contains(product.ListingId)))
            .Select(ProductResponse.From)
            .ToArray();

        return Results.Ok(products);
    }

    /// <summary>
    /// Streams the artwork that illustrates a digital product.
    /// </summary>
    /// <remarks>
    /// The image is proxied rather than linked: the container stays private, no blob name or
    /// SAS token reaches the browser, and the response is cacheable by the browser for a day.
    /// A missing image is a normal state, not an error — the storefront falls back to its
    /// generated artwork.
    /// </remarks>
    /// <param name="id">Product whose artwork should be returned.</param>
    /// <param name="request">Current request, used for conditional revalidation.</param>
    /// <param name="response">Current response, used to set caching headers.</param>
    /// <param name="catalog">Catalog used to resolve the product.</param>
    /// <param name="images">Storage-backed source of the artwork.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    private static async Task<IResult> GetProductThumbnailAsync(
        Guid id,
        HttpRequest request,
        HttpResponse response,
        IProductCatalog catalog,
        IProductImageService images,
        CancellationToken cancellationToken)
    {
        if (!catalog.TryGetProduct(id, out var product) || !product.IsDigitalPdf)
        {
            return TypedResults.NotFound();
        }

        var image = await images.OpenThumbnailAsync(product, cancellationToken);
        if (image is null)
        {
            return TypedResults.NotFound();
        }

        var ifNoneMatch = request.Headers.IfNoneMatch.ToString();
        if (ifNoneMatch.Length > 0 && ifNoneMatch.Contains(image.ETag, StringComparison.Ordinal))
        {
            await image.Content.DisposeAsync().ConfigureAwait(false);
            return TypedResults.StatusCode(StatusCodes.Status304NotModified);
        }

        response.Headers.CacheControl = "public, max-age=86400";
        response.Headers.ETag = image.ETag;

        return Results.Stream(image.Content, image.ContentType);
    }
}

/// <summary>
/// A product as the storefront sees it. Never includes blob names or other storage details.
/// </summary>
/// <param name="Id">Stable product identifier used by checkout.</param>
/// <param name="Name">Customer-facing name.</param>
/// <param name="Description">Short sales copy for the product card.</param>
/// <param name="Price">Unit price in the store currency.</param>
/// <param name="Fulfillment">Fulfillment type: <c>DigitalPdf</c> or <c>PhysicalMerch</c>.</param>
/// <param name="Category">Browse category, or <see langword="null"/> for merchandise.</param>
/// <param name="ListingId">Listing that backs the PDF, or <see langword="null"/> for merchandise.</param>
/// <param name="ThumbnailUrl">Relative URL of the product artwork, or <see langword="null"/> when there is none.</param>
/// <param name="Accent">Card artwork colours.</param>
public sealed record ProductResponse(
    Guid Id,
    string Name,
    string? Description,
    decimal Price,
    string Fulfillment,
    string? Category,
    string? ListingId,
    string? ThumbnailUrl,
    ProductAccentResponse? Accent)
{
    /// <summary>
    /// Projects a catalog product onto the public response shape.
    /// </summary>
    /// <param name="product">The catalog product.</param>
    /// <returns>The public representation of <paramref name="product"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="product"/> is <see langword="null"/>.</exception>
    public static ProductResponse From(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);

        return new ProductResponse(
            product.Id,
            product.Name,
            product.Description,
            product.Price,
            product.Fulfillment.ToString(),
            product.Category,
            product.ListingId,
            product.IsDigitalPdf ? $"/api/products/{product.Id}/thumbnail" : null,
            product.Accent is null
                ? null
                : new ProductAccentResponse(product.Accent.From, product.Accent.To, product.Accent.Ink));
    }
}

/// <summary>
/// Card artwork colours for a product.
/// </summary>
/// <param name="From">Starting colour of the artwork gradient.</param>
/// <param name="To">Ending colour of the artwork gradient.</param>
/// <param name="Ink">Foreground colour for the icon.</param>
public sealed record ProductAccentResponse(string From, string To, string Ink);
