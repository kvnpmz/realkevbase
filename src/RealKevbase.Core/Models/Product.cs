namespace RealKevbase.Core.Models;

/// <summary>
/// Specifies how a <see cref="Product"/> reaches the customer after payment succeeds.
/// </summary>
public enum FulfillmentType
{
    /// <summary>
    /// The product is delivered as a downloadable PDF stored in Azure Blob Storage.
    /// </summary>
    DigitalPdf = 0,

    /// <summary>
    /// The product is delivered as physical merchandise that has to be shipped.
    /// </summary>
    PhysicalMerch = 1
}

/// <summary>
/// A purchasable item in the RealKevbase catalog.
/// </summary>
/// <remarks>
/// Digital PDF products carry the name of their blob in <see cref="PdfBlobName"/>, which
/// <see cref="Services.IPdfStorageService"/> turns into a time-limited download URL.
/// Physical merchandise has no blob and therefore leaves <see cref="PdfBlobName"/> empty.
/// </remarks>
public sealed record Product
{
    /// <summary>
    /// Gets the stable product identifier.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Gets the customer-facing product name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the short sales copy shown on the product card.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the storefront browse category, for example <c>moving-out</c>.
    /// <see langword="null"/> for physical merchandise, which is not part of a category.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// Gets the card artwork colours the storefront renders this product with.
    /// </summary>
    public ProductAccent? Accent { get; init; }

    /// <summary>
    /// Gets the unit price in the store's checkout currency.
    /// </summary>
    public required decimal Price { get; init; }

    /// <summary>
    /// Gets how the product is fulfilled. Distinguishes <see cref="FulfillmentType.DigitalPdf"/>
    /// from <see cref="FulfillmentType.PhysicalMerch"/> so checkout and webhook handling can branch
    /// without inspecting <see cref="PdfBlobName"/>.
    /// </summary>
    public required FulfillmentType Fulfillment { get; init; }

    /// <summary>
    /// Gets the name of the PDF blob inside the configured Azure Blob Storage container,
    /// for example <c>guides/field-guide.pdf</c>.
    /// Required for <see cref="FulfillmentType.DigitalPdf"/>; <see langword="null"/> for
    /// <see cref="FulfillmentType.PhysicalMerch"/>.
    /// </summary>
    public string? PdfBlobName { get; init; }

    /// <summary>
    /// Gets a value indicating whether the product is fulfilled as a digital PDF download.
    /// </summary>
    public bool IsDigitalPdf => Fulfillment == FulfillmentType.DigitalPdf;

    /// <summary>
    /// Gets the identifier of the listing that produced this product's PDF, or
    /// <see langword="null"/> for merchandise.
    /// </summary>
    /// <remarks>
    /// The listing identifier is the blob file name without its extension, with hyphens
    /// replaced by underscores, which matches the <c>Projects.id</c> column of the listing
    /// database (for example <c>guides/first-apartment.pdf</c> becomes <c>first_apartment</c>).
    /// </remarks>
    public string? ListingId =>
        string.IsNullOrWhiteSpace(PdfBlobName)
            ? null
            : Path.GetFileNameWithoutExtension(PdfBlobName).Replace('-', '_');

    /// <summary>
    /// Verifies that the product is internally consistent before it is used to create a
    /// checkout session or a download link.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the product identifier is empty, the name is blank, the price is negative,
    /// a digital PDF product has no <see cref="PdfBlobName"/>, or physical merchandise does have one.
    /// </exception>
    public void Validate()
    {
        if (Id == Guid.Empty)
        {
            throw new InvalidOperationException("A product must have a non-empty identifier.");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidOperationException($"Product '{Id}' must have a name.");
        }

        if (Price < 0)
        {
            throw new InvalidOperationException($"Product '{Id}' must not have a negative price.");
        }

        switch (Fulfillment)
        {
            case FulfillmentType.DigitalPdf when string.IsNullOrWhiteSpace(PdfBlobName):
                throw new InvalidOperationException(
                    $"Product '{Id}' is fulfilled as {FulfillmentType.DigitalPdf} and must specify a {nameof(PdfBlobName)}.");

            case FulfillmentType.PhysicalMerch when PdfBlobName is not null:
                throw new InvalidOperationException(
                    $"Product '{Id}' is fulfilled as {FulfillmentType.PhysicalMerch} and must not specify a {nameof(PdfBlobName)}.");

            case FulfillmentType.DigitalPdf:
            case FulfillmentType.PhysicalMerch:
                break;

            default:
                throw new InvalidOperationException(
                    $"Product '{Id}' has unsupported fulfillment type '{Fulfillment}'.");
        }
    }
}
