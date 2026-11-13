using RealKevbase.Core.Models;

namespace RealKevbase.Core.Services;

/// <summary>
/// Issues pre-signed, read-only download URLs for digital PDF products held in Azure Blob Storage.
/// </summary>
/// <remarks>
/// Every URL is a SAS token bound to a single blob and expires after <see cref="LinkLifetime"/>
/// (24 hours), so the storage account key is never exposed to the browser.
/// </remarks>
public interface IPdfStorageService
{
    /// <summary>
    /// Gets the lifetime of every issued download URL. This is always 24 hours.
    /// </summary>
    TimeSpan LinkLifetime { get; }

    /// <summary>
    /// Creates a read-only pre-signed download URL for the PDF of a digital product.
    /// </summary>
    /// <param name="product">The digital PDF product whose <see cref="Product.PdfBlobName"/> is signed.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>An absolute URL that is valid for <see cref="LinkLifetime"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="product"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the product fails <see cref="Product.Validate"/> or is not fulfilled as
    /// <see cref="FulfillmentType.DigitalPdf"/>.
    /// </exception>
    Task<Uri> CreateDownloadUrlAsync(Product product, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a read-only pre-signed download URL for a single blob.
    /// Used by payment webhook fulfillment, where only the stored blob name is available.
    /// </summary>
    /// <param name="blobName">Name of the blob inside the configured PDF container.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>An absolute URL that is valid for <see cref="LinkLifetime"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="blobName"/> is null, empty or whitespace.</exception>
    Task<Uri> CreateDownloadUrlAsync(string blobName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a download URL for the full-resolution page image that belongs to a PDF,
    /// when the listing published one.
    /// </summary>
    /// <remarks>
    /// The image is an optional companion, like the A4 edition: older listings may have no
    /// render, and a missing image must never fail a paid order. The URL is served as an
    /// attachment so a phone downloads the file instead of opening it in a new tab.
    /// </remarks>
    /// <param name="pdfBlobName">
    /// Blob name of the PDF whose image should be signed, for example <c>guides/foo.pdf</c>.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>
    /// A read-only URL valid for <see cref="LinkLifetime"/>, or <see langword="null"/> when
    /// the PDF names no image or that image was never published.
    /// </returns>
    Task<Uri?> TryCreateImageDownloadUrlAsync(
        string pdfBlobName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the editions a buyer of this product receives, Letter first.
    /// </summary>
    /// <remarks>
    /// Only the Letter PDF is guaranteed; the A4 edition exists for older listings and is
    /// included when its blob is published beside the Letter PDF. Nothing is inferred from a
    /// preview render, so a link is never issued for a file that was never uploaded.
    /// </remarks>
    /// <param name="product">The digital PDF product whose editions should be listed.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>
    /// The editions that exist in storage. Empty when the product has no PDF blob, which
    /// callers should treat as a broken listing rather than as a free product.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="product"/> is <see langword="null"/>.</exception>
    Task<IReadOnlyList<PdfEdition>> GetEditionsAsync(
        Product product,
        CancellationToken cancellationToken = default);
}
