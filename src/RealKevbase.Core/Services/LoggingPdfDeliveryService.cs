using Microsoft.Extensions.Logging;

using RealKevbase.Core.Models;

namespace RealKevbase.Core.Services;

/// <summary>
/// Placeholder delivery implementation used until an email or order-storage provider is
/// configured. It records that links were issued but intentionally does not log the
/// pre-signed URLs, because those URLs are bearer credentials.
/// </summary>
public sealed class LoggingPdfDeliveryService : IPdfDeliveryService
{
    private readonly ILogger<LoggingPdfDeliveryService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoggingPdfDeliveryService"/> class.
    /// </summary>
    /// <param name="logger">Logger used to record issuance.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <see langword="null"/>.</exception>
    public LoggingPdfDeliveryService(ILogger<LoggingPdfDeliveryService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public Task DeliverAsync(PdfDelivery delivery, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        _logger.LogWarning(
            "Issued {DownloadCount} PDF download link(s) for Stripe session {SessionId}, but no delivery provider is configured; no email was sent (buyer email present: {HasBuyerEmail}).",
            delivery.Downloads.Count,
            delivery.SessionId,
            !string.IsNullOrWhiteSpace(delivery.CustomerEmail));

        return Task.CompletedTask;
    }
}
