namespace RealKevbase.Core.Models;

/// <summary>
/// A cart line that has been validated against the product catalog: the product itself
/// (with its authoritative price) and the quantity ordered.
/// </summary>
/// <param name="Product">Product resolved from the catalog.</param>
/// <param name="Quantity">Positive quantity ordered.</param>
public sealed record CheckoutLine(Product Product, long Quantity);

/// <summary>
/// The Stripe Checkout Session created for a cart.
/// </summary>
/// <param name="SessionId">Stripe Checkout Session identifier.</param>
/// <param name="Url">Stripe-hosted checkout URL to redirect the buyer to.</param>
public sealed record CheckoutSessionResult(string SessionId, string? Url);
