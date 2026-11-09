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

    /// <inheritdoc />
    public async Task<ProductImage?> OpenThumbnailAsync(Product product, CancellationToken cancellationToken = default)
    {
        var blobName = GetThumbnailBlobName(product);
        if (blobName is null)
        {
            return null;
        }

        var blob = _containerClient.GetBlobClient(blobName);
        if (!await blob.ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var download = await blob
            .DownloadStreamingAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var result = download.Value;

        return new ProductImage(
            result.Content,
            string.IsNullOrWhiteSpace(result.Details.ContentType) ? "image/webp" : result.Details.ContentType,
            result.Details.ETag.ToString());
    }
}
