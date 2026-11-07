using RealKevbase.Core.Services;

namespace RealKevbase.Api.Services;

/// <summary>
/// Background worker that drains <see cref="ICheckoutFulfillmentQueue"/> and issues PDF
/// download links for paid sessions, keeping fulfillment off the webhook request path.
/// </summary>
/// <remarks>
/// A failed session is logged and skipped rather than retried forever; because links are
/// idempotent to re-issue, a durable retry can be added later without changing the contract.
/// </remarks>
public sealed class CheckoutFulfillmentWorker : BackgroundService
{
    private readonly ICheckoutFulfillmentQueue _queue;
    private readonly IDigitalFulfillmentService _fulfillment;
    private readonly ILogger<CheckoutFulfillmentWorker> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CheckoutFulfillmentWorker"/> class.
    /// </summary>
    /// <param name="queue">Queue of paid sessions.</param>
    /// <param name="fulfillment">Issuer of download links.</param>
    /// <param name="logger">Logger for worker outcomes.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null"/>.</exception>
    public CheckoutFulfillmentWorker(
        ICheckoutFulfillmentQueue queue,
        IDigitalFulfillmentService fulfillment,
        ILogger<CheckoutFulfillmentWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(fulfillment);
        ArgumentNullException.ThrowIfNull(logger);

        _queue = queue;
        _fulfillment = fulfillment;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Checkout fulfillment worker started.");

        try
        {
            await foreach (var sessionId in _queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await _fulfillment.FulfillAsync(sessionId, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _logger.LogError(
                        exception,
                        "PDF fulfillment failed for Stripe session {SessionId}.",
                        sessionId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }
}
