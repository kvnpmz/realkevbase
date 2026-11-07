using RealKevbase.Core.Models;

namespace RealKevbase.Core.Services;

/// <summary>
/// Fulfills a paid Stripe Checkout Session by issuing PDF download links for its digital
/// products and handing them to <see cref="IPdfDeliveryService"/>.
/// </summary>
/// <remarks>
/// Runs in a background worker, never in the webhook HTTP request, so Stripe receives its
/// acknowledgement immediately. The same issuing step also backs the buyer-facing
/// success-page downloads endpoint.
/// </remarks>
public interface IDigitalFulfillmentService
{
    /// <summary>
    /// Issues downloads for a paid session. Safe to call more than once for the same
    /// session: links are read-only and short-lived, so a retry only re-issues them.
    /// </summary>
    /// <param name="sessionId">Stripe Checkout Session id from the webhook event.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when delivery has been handed off.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sessionId"/> is null or blank.</exception>
    Task FulfillAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Issues 24-hour download links for a session without delivering them anywhere.
    /// </summary>
    /// <remarks>
    /// The session is always re-read from Stripe, so an unpaid or unknown session never
    /// yields links. Download links are bearer credentials: callers must not log them.
    /// </remarks>
    /// <param name="sessionId">Stripe Checkout Session id.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>
    /// The issued downloads, with an empty collection when the order contains no digital
    /// products, or <see langword="null"/> when the session has not been paid.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sessionId"/> is null or blank.</exception>
    Task<PdfDelivery?> IssueAsync(string sessionId, CancellationToken cancellationToken = default);
}
