using RealKevbase.Core.Models;

namespace RealKevbase.Core.Services;

/// <summary>
/// Creates Stripe Checkout Sessions for validated carts, supporting mixed orders of
/// digital PDFs and physical merchandise in a single payment.
/// </summary>
public interface IStripeCheckoutService
{
    /// <summary>
    /// Creates a Stripe Checkout Session for the supplied lines.
    /// </summary>
    /// <param name="lines">Cart lines resolved from <see cref="IProductCatalog"/>. Prices are taken from each product.</param>
    /// <param name="cancellationToken">Token used to cancel the Stripe request.</param>
    /// <returns>The session id and hosted checkout URL.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="lines"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="lines"/> is empty or contains an invalid quantity.</exception>
    /// <exception cref="InvalidOperationException">Thrown when required Stripe configuration is missing.</exception>
    /// <exception cref="Stripe.StripeException">Thrown when Stripe rejects the request.</exception>
    Task<CheckoutSessionResult> CreateSessionAsync(
        IReadOnlyCollection<CheckoutLine> lines,
        CancellationToken cancellationToken = default);
}
