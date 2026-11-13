using RealKevbase.Core.Services;

namespace RealKevbase.Core.Models;

/// <summary>
/// The kind of file a download link serves.
/// </summary>
/// <remarks>
/// Every paid guide ships its PDF, and the full-resolution page image that the storefront
/// cover is downscaled from, so one edition can hand over two files.
/// </remarks>
public enum DeliveryFormat
{
    /// <summary>
    /// The print-ready PDF the guide was laid out as.
    /// </summary>
    Pdf = 0,

    /// <summary>
    /// The full-resolution PNG of the page, for note apps and design tools.
    /// </summary>
    Image = 1
}

/// <summary>
/// A pre-signed download for one file of a purchased guide.
/// </summary>
/// <param name="Product">The digital product.</param>
/// <param name="Edition">Paper edition this file belongs to.</param>
/// <param name="Format">Whether the link serves the PDF or the page image.</param>
/// <param name="Url">Read-only SAS URL, valid for <see cref="Services.IPdfStorageService.LinkLifetime"/>.</param>
/// <param name="ExpiresAt">Moment the URL stops working.</param>
public sealed record PdfDownload(
    Product Product,
    PdfEdition Edition,
    DeliveryFormat Format,
    Uri Url,
    DateTimeOffset ExpiresAt);

/// <summary>
/// The set of downloads issued for a paid checkout session, ready to be delivered to the
/// buyer.
/// </summary>
/// <param name="SessionId">Stripe Checkout Session that was paid.</param>
/// <param name="CustomerEmail">Buyer email collected by Stripe, when available.</param>
/// <param name="Downloads">One entry per file in the order: each edition contributes its
/// PDF and, when it has one, the full-resolution PNG. A listing with both a Letter and an
/// A4 edition can therefore yield up to four links.</param>
public sealed record PdfDelivery(
    string SessionId,
    string? CustomerEmail,
    IReadOnlyCollection<PdfDownload> Downloads)
{
    /// <summary>
    /// Gets the number of PDF files in the order.
    /// </summary>
    /// <remarks>
    /// Buyer-facing copy counts guides, not links: a guide that ships a PNG beside its PDF
    /// must not read as two PDFs.
    /// </remarks>
    public int PdfCount => Downloads.Count(download => download.Format == DeliveryFormat.Pdf);

    /// <summary>
    /// Gets the number of distinct products in the order.
    /// </summary>
    public int ProductCount => Downloads.Select(download => download.Product.Id).Distinct().Count();
}
