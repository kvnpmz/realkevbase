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
    /// <returns>The same <paramref name="endpoints"/> instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoints"/> is <see langword="null"/>.</exception>
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/api").WithTags("Catalog");

        group.MapGet("/products", GetProductsAsync)
            .WithName("GetProducts")
            .WithSummary("Returns every purchasable product, without storage details.")
            .Produces<IReadOnlyList<ProductResponse>>();

        group.MapGet("/products/{id:guid}/thumbnail", GetProductThumbnailAsync)
            .WithName("GetProductThumbnail")
            .WithSummary("Streams the artwork for a digital product.")
            .Produces(StatusCodes.Status200OK, contentType: "image/webp")
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/products/{id:guid}/images/{slot:int}", GetProductGalleryImageAsync)
            .WithName("GetProductGalleryImage")
            .WithSummary("Streams one gallery image for a digital product.")
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
    private static async Task<IResult> GetProductsAsync(
        HttpResponse response,
        IProductCatalog catalog,
        IListingRepository listings,
        IProductImageService images,
        CancellationToken cancellationToken)
    {
        var available = listings.GetAll();
        var listingIds = available?.Select(listing => listing.Id).ToHashSet(StringComparer.Ordinal);

        var offered = catalog.Products
            .Where(product => !product.IsDigitalPdf
                              || listingIds is null
                              || (product.ListingId is not null && listingIds.Contains(product.ListingId)))
            .ToArray();

        // One cached folder listing tells the storefront which renders and editions exist, so
        // the detail page asks only for the images it will actually get.
        var published = await images.DescribeArtworkAsync(offered, cancellationToken);

        var products = offered
            .Select(product => ProductResponse.From(
                product,
                published is not null && published.TryGetValue(product.Id, out var artwork) ? artwork : null))
            .ToArray();

        // The catalog only changes on deploy, so a short browser cache removes a round trip
        // for a returning visitor without hiding a new product for long.
        response.Headers.CacheControl = "public, max-age=60";

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
        return image is null ? TypedResults.NotFound() : await StreamImageAsync(image, request, response);
    }

    /// <summary>
    /// Streams one image of a digital product's gallery.
    /// </summary>
    /// <remarks>
    /// Slots are positional rather than named so the storefront never learns a blob name:
    /// slot 0 is the card thumbnail, slots 1 to 3 are the extra renders the generator
    /// produces. A slot the listing never rendered is a normal 404, so the detail page can
    /// request every slot and simply drop the ones that are missing.
    /// </remarks>
    /// <param name="id">Product whose artwork should be returned.</param>
    /// <param name="slot">Gallery slot, from 0 to <see cref="IProductImageService.GallerySlotCount"/> - 1.</param>
    /// <param name="request">Current request, used for conditional revalidation.</param>
    /// <param name="response">Current response, used to set caching headers.</param>
    /// <param name="catalog">Catalog used to resolve the product.</param>
    /// <param name="images">Storage-backed source of the artwork.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    private static async Task<IResult> GetProductGalleryImageAsync(
        Guid id,
        int slot,
        HttpRequest request,
        HttpResponse response,
        IProductCatalog catalog,
        IProductImageService images,
        CancellationToken cancellationToken)
    {
        if (slot < 0
            || slot >= IProductImageService.GallerySlotCount
            || !catalog.TryGetProduct(id, out var product)
            || !product.IsDigitalPdf)
        {
            return TypedResults.NotFound();
        }

        var image = await images.OpenGalleryImageAsync(product, slot, cancellationToken);
        return image is null ? TypedResults.NotFound() : await StreamImageAsync(image, request, response);
    }

    /// <summary>
    /// Writes a product image with cache validators.
    /// </summary>
    /// <param name="image">Open image to stream.</param>
    /// <param name="request">Current request, used for conditional revalidation.</param>
    /// <param name="response">Current response, used to set caching headers.</param>
    private static async Task<IResult> StreamImageAsync(
        ProductImage image,
        HttpRequest request,
        HttpResponse response)
    {
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
/// <param name="GallerySlots">
/// Gallery slots that can be served, or <see langword="null"/> when storage could not be
/// listed; the storefront then requests every slot and drops the ones that answer 404.
/// </param>
/// <param name="PageSizes">Paper sizes the product is laid out for, or <see langword="null"/> for merchandise.</param>
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
    IReadOnlyList<int>? GallerySlots,
    IReadOnlyList<string>? PageSizes,
    ProductAccentResponse? Accent)
{
    /// <summary>
    /// Projects a catalog product onto the public response shape.
    /// </summary>
    /// <param name="product">The catalog product.</param>
    /// <param name="artwork">
    /// What storage holds for the product, or <see langword="null"/> when that is unknown.
    /// </param>
    /// <returns>The public representation of <paramref name="product"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="product"/> is <see langword="null"/>.</exception>
    public static ProductResponse From(Product product, PublishedArtwork? artwork)
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
            product.IsDigitalPdf ? artwork?.GallerySlots : null,
            product.IsDigitalPdf ? artwork?.PageSizes : null,
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
