using RealKevbase.Core.Models;

namespace RealKevbase.Core.Services;

/// <summary>
/// Read-only access to the products that may be purchased.
/// </summary>
/// <remarks>
/// Checkout must always resolve prices and fulfillment types from here, never from the
/// request body, so a client cannot influence what it is charged.
/// </remarks>
public interface IProductCatalog
{
    /// <summary>
    /// Gets every product currently offered for sale.
    /// </summary>
    IReadOnlyCollection<Product> Products { get; }

    /// <summary>
    /// Looks up a single product.
    /// </summary>
    /// <param name="id">Product identifier.</param>
    /// <param name="product">The matching product when found; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the product exists.</returns>
    bool TryGetProduct(Guid id, out Product product);
}
