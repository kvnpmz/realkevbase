using System.Threading.Channels;

namespace RealKevbase.Api.Services;

/// <summary>
/// In-process queue of paid Stripe sessions waiting for PDF fulfillment.
/// </summary>
/// <remarks>
/// Bounded so a traffic spike cannot exhaust memory. When the queue is full the webhook
/// returns 503 and Stripe retries, preserving the "acknowledge fast, fulfil later" contract.
/// The queue is intentionally in-memory: a process restart loses queued work, so a durable
/// queue (storage queue / database outbox) is the next step before real traffic.
/// </remarks>
public interface ICheckoutFulfillmentQueue
{
    /// <summary>
    /// Attempts to enqueue a paid session without blocking.
    /// </summary>
    /// <param name="sessionId">Stripe Checkout Session id.</param>
    /// <returns><see langword="true"/> when the session was queued; <see langword="false"/> when the queue is full.</returns>
    bool TryEnqueue(string sessionId);

    /// <summary>
    /// Reads queued sessions until the worker stops.
    /// </summary>
    /// <param name="cancellationToken">Token used to stop reading.</param>
    /// <returns>An async stream of session ids.</returns>
    IAsyncEnumerable<string> ReadAllAsync(CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class CheckoutFulfillmentQueue : ICheckoutFulfillmentQueue
{
    private const int Capacity = 100;

    private readonly Channel<string> _channel = Channel.CreateBounded<string>(
        new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });

    /// <inheritdoc />
    public bool TryEnqueue(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return _channel.Writer.TryWrite(sessionId);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<string> ReadAllAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
