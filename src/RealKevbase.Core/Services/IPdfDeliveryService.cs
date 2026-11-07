using RealKevbase.Core.Models;

namespace RealKevbase.Core.Services;

/// <summary>
/// Delivers issued PDF download links to the buyer.
/// </summary>
/// <remarks>
/// Delivery is separated from link issuance so the fulfillment worker never depends on an
/// email provider. Replace <see cref="LoggingPdfDeliveryService"/> with an implementation
/// that sends email (or stores the links against an order) when that infrastructure exists.
/// </remarks>
public interface IPdfDeliveryService
{
    /// <summary>
    /// Sends the download links to the buyer.
    /// </summary>
    /// <param name="delivery">Session, buyer and issued downloads.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when delivery has been handed off.</returns>
    Task DeliverAsync(PdfDelivery delivery, CancellationToken cancellationToken = default);
}
