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
}
