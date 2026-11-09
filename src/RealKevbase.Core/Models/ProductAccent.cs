namespace RealKevbase.Core.Models;

/// <summary>
/// The card artwork colours used to present a product in the storefront.
/// </summary>
/// <remarks>
/// These are presentation values owned by the catalog so that the storefront holds no product
/// data of its own: the SPA renders whatever the API returns. Each value is a CSS colour.
/// </remarks>
/// <param name="From">Starting colour of the card artwork gradient.</param>
/// <param name="To">Ending colour of the card artwork gradient.</param>
/// <param name="Ink">Foreground colour for the icon drawn on the artwork.</param>
public sealed record ProductAccent(string From, string To, string Ink);
