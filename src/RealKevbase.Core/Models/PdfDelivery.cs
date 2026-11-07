using RealKevbase.Core.Services;

namespace RealKevbase.Core.Models;

/// <summary>
/// A pre-signed download for one purchased PDF.
/// </summary>
/// <param name="Product">The digital product.</param>
/// <param name="Url">Read-only SAS URL, valid for <see cref="Services.IPdfStorageService.LinkLifetime"/>.</param>
/// <param name="ExpiresAt">Moment the URL stops working.</param>
public sealed record PdfDownload(Product Product, Uri Url, DateTimeOffset ExpiresAt);

/// <summary>
/// The set of PDF downloads issued for a paid checkout session, ready to be delivered
/// to the buyer.
/// </summary>
/// <param name="SessionId">Stripe Checkout Session that was paid.</param>
/// <param name="CustomerEmail">Buyer email collected by Stripe, when available.</param>
/// <param name="Downloads">One entry per digital product in the order.</param>
public sealed record PdfDelivery(
    string SessionId,
    string? CustomerEmail,
    IReadOnlyCollection<PdfDownload> Downloads);
