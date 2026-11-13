using Azure;
using Azure.Storage.Blobs;

using RealKevbase.Core.Models;

namespace RealKevbase.Core.Services;

/// <summary>
/// A product thumbnail read from blob storage, together with the metadata needed to
/// serve it without another round trip.
/// </summary>
/// <param name="Content">Stream positioned at the start of the image bytes.</param>
/// <param name="ContentType">MIME type recorded on the blob, for example <c>image/webp</c>.</param>
/// <param name="ETag">Validator for conditional requests, quoted as blob storage returns it.</param>
public sealed record ProductImage(Stream Content, string ContentType, string ETag);

/// <summary>
/// Reads the artwork that illustrates a digital product.
/// </summary>
/// <remarks>
/// Thumbnails live beside the PDFs they illustrate, under an <c>images/</c> folder, so a
/// product and its artwork ship together. The container stays private and the blob name is
/// never returned to the browser: callers stream the bytes through this service instead.
/// </remarks>
public interface IProductImageService
{
    /// <summary>
    /// Number of gallery slots a product can expose, in display order.
    /// </summary>
    /// <remarks>
    /// Slot 0 is the same artwork the storefront uses as the card thumbnail; slots 1 to 3 are
    /// the detail zoom, the tablet mockup and the A4 preview that the generator renders beside
    /// it. Not every listing has all four, so callers must treat a missing slot as normal.
    /// </remarks>
    const int GallerySlotCount = 4;

    /// <summary>
    /// Opens the thumbnail for a digital product.
    /// </summary>
    /// <param name="product">Product whose artwork should be read.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>
    /// The image, or <see langword="null"/> when the product has no PDF, has no artwork, or
    /// the artwork has not been published to storage yet.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="product"/> is <see langword="null"/>.</exception>
    Task<ProductImage?> OpenThumbnailAsync(Product product, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens one image of a digital product's gallery.
    /// </summary>
    /// <param name="product">Product whose artwork should be read.</param>
    /// <param name="slot">Gallery slot, from 0 to <see cref="GallerySlotCount"/> - 1.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>
    /// The image, or <see langword="null"/> when the slot is out of range, the product has no
    /// PDF, or that render was never published. A missing render is a normal state: the
    /// gallery shows the ones that exist.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="product"/> is <see langword="null"/>.</exception>
    Task<ProductImage?> OpenGalleryImageAsync(
        Product product,
        int slot,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads product thumbnails from the same private container that holds the PDFs.
/// </summary>
/// <remarks>
/// The blob name is derived from <see cref="Product.PdfBlobName"/>, so the generator's layout
/// (<c>guides/foo.pdf</c> with artwork at <c>guides/images/foo.webp</c>) is the only convention
/// that has to be maintained. Nothing here is signed: the bytes are proxied by the API, which
/// keeps the storage account private and avoids handing out URLs that outlive the request.
/// </remarks>
public sealed class AzureBlobProductImageService : IProductImageService
{
    /// <summary>
    /// Folder, relative to the PDF's own folder, that holds the artwork.
    /// </summary>
    public const string ThumbnailFolderName = "images";

    /// <summary>
    /// File extension of the published thumbnails.
    /// </summary>
    public const string ThumbnailExtension = ".webp";

    /// <summary>
    /// File-name suffixes of the gallery renders, in the order the storefront shows them.
    /// Mirrors the variants the PDF generator writes beside each page.
    /// </summary>
    private static readonly string[] GallerySuffixes = ["", "-zoom", "-ipad", "-a4"];

    private readonly BlobContainerClient _containerClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureBlobProductImageService"/> class.
    /// </summary>
    /// <param name="blobServiceClient">Client for the storage account.</param>
    /// <param name="options">Container configuration shared with the PDF storage service.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null"/>.</exception>
    public AzureBlobProductImageService(BlobServiceClient blobServiceClient, AzurePdfStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(blobServiceClient);
        ArgumentNullException.ThrowIfNull(options);

        _containerClient = blobServiceClient.GetBlobContainerClient(options.PdfContainerName);
    }

    /// <summary>
    /// Derives the thumbnail blob name for a product.
    /// </summary>
    /// <param name="product">Product whose artwork location should be computed.</param>
    /// <returns>
    /// The blob name, for example <c>guides/images/foo.webp</c> for a PDF at
    /// <c>guides/foo.pdf</c>, or <see langword="null"/> when the product has no PDF blob.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="product"/> is <see langword="null"/>.</exception>
    public static string? GetThumbnailBlobName(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);

        if (string.IsNullOrWhiteSpace(product.PdfBlobName))
        {
            return null;
        }

        // Blob names always use forward slashes, whatever the host platform separator is.
        var blobName = product.PdfBlobName.Replace('\\', '/');
        var lastSlash = blobName.LastIndexOf('/');
        var folder = lastSlash >= 0 ? blobName[..lastSlash] : string.Empty;
        var file = lastSlash >= 0 ? blobName[(lastSlash + 1)..] : blobName;
        var stem = Path.GetFileNameWithoutExtension(file);

        if (string.IsNullOrEmpty(stem))
        {
            return null;
        }

        var thumbnail = $"{stem}{ThumbnailExtension}";

        return string.IsNullOrEmpty(folder)
            ? $"{ThumbnailFolderName}/{thumbnail}"
            : $"{folder}/{ThumbnailFolderName}/{thumbnail}";
    }

    /// <summary>
    /// Derives the gallery blob name for a product and slot.
    /// </summary>
    /// <param name="product">Product whose artwork location should be computed.</param>
    /// <param name="slot">Gallery slot, from 0 to <see cref="IProductImageService.GallerySlotCount"/> - 1.</param>
    /// <returns>
    /// The blob name, for example <c>guides/images/foo-zoom.webp</c> for slot 1, or
    /// <see langword="null"/> when the slot is out of range or the product has no PDF blob.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="product"/> is <see langword="null"/>.</exception>
    public static string? GetGalleryBlobName(Product product, int slot)
    {
        ArgumentNullException.ThrowIfNull(product);

        if (slot < 0 || slot >= GallerySuffixes.Length)
        {
            return null;
        }

        var thumbnail = GetThumbnailBlobName(product);
        if (thumbnail is null)
        {
            return null;
        }

        // Thumbnail names always end in the published image extension, so the suffix can be
        // inserted in front of it without re-deriving the folder and stem.
        var stem = thumbnail[..^ThumbnailExtension.Length];
        return $"{stem}{GallerySuffixes[slot]}{ThumbnailExtension}";
    }

    /// <inheritdoc />
    public Task<ProductImage?> OpenThumbnailAsync(Product product, CancellationToken cancellationToken = default)
    {
        var blobName = GetThumbnailBlobName(product);
        return blobName is null ? Task.FromResult<ProductImage?>(null) : OpenAsync(blobName, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ProductImage?> OpenGalleryImageAsync(
        Product product,
        int slot,
        CancellationToken cancellationToken = default)
    {
        var blobName = GetGalleryBlobName(product, slot);
        return blobName is null ? Task.FromResult<ProductImage?>(null) : OpenAsync(blobName, cancellationToken);
    }

    /// <summary>
    /// Streams an image blob.
    /// </summary>
    /// <param name="blobName">Blob name inside the shared container.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The image, or <see langword="null"/> when the blob does not exist.</returns>
    private async Task<ProductImage?> OpenAsync(string blobName, CancellationToken cancellationToken)
    {
        var blob = _containerClient.GetBlobClient(blobName);

        try
        {
            // One round trip, not two: the download itself reports a missing blob, so no
            // separate ExistsAsync probe is needed. A gallery view would otherwise double
            // the storage calls made per detail page.
            var download = await blob
                .DownloadStreamingAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var result = download.Value;

            return new ProductImage(
                result.Content,
                string.IsNullOrWhiteSpace(result.Details.ContentType) ? "image/webp" : result.Details.ContentType,
                result.Details.ETag.ToString());
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            // A render the listing never produced is a normal state for gallery slots.
            return null;
        }
    }
}
