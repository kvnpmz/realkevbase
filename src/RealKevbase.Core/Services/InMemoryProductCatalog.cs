using RealKevbase.Core.Models;

namespace RealKevbase.Core.Services;

/// <summary>
/// In-memory product catalog used until the product data is moved into a database.
/// </summary>
/// <remarks>
/// The identifiers mirror the demo catalog rendered by the SPA so that a cart built in the
/// storefront validates end to end. Every entry is validated on construction, which makes a
/// misconfigured product (for example a PDF without a blob name) fail at startup rather than
/// during checkout.
/// </remarks>
public sealed class InMemoryProductCatalog : IProductCatalog
{
    private static readonly Product[] Seed =
    [
        new()
        {
            Id = Guid.Parse("6f1b6a9e-2f4c-4f1e-9a1d-0c2b3e4d5f60"),
            Name = "The RealKevbase Field Guide",
            Price = 19.99m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/field-guide.pdf"
        },
        new()
        {
            Id = Guid.Parse("b2f4a1c8-5d6e-4a7b-8c9d-1e2f3a4b5c6d"),
            Name = "The Launch Playbook",
            Price = 29.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/launch-playbook.pdf"
        },
        new()
        {
            Id = Guid.Parse("c3a5b2d9-6e7f-4b8c-9d0e-2f3a4b5c6d7e"),
            Name = "Systems Notebook",
            Price = 12.50m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/systems-notebook.pdf"
        },
        new()
        {
            Id = Guid.Parse("9c2d4e6f-8a0b-4c1d-9e3f-5a7b9c1d3e5f"),
            Name = "RealKevbase Logo Tee",
            Price = 24.00m,
            Fulfillment = FulfillmentType.PhysicalMerch
        },
        new()
        {
            Id = Guid.Parse("d4b6c3e1-7f80-4c9d-8e1f-3a4b5c6d7e8f"),
            Name = "Heavyweight Hoodie",
            Price = 59.00m,
            Fulfillment = FulfillmentType.PhysicalMerch
        },
        new()
        {
            Id = Guid.Parse("e5c7d4f2-8091-4d0e-9f2a-4b5c6d7e8f90"),
            Name = "Studio Desk Mat",
            Price = 34.00m,
            Fulfillment = FulfillmentType.PhysicalMerch
        }
    ];

    private readonly Dictionary<Guid, Product> _products;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryProductCatalog"/> class and
    /// validates every seeded product.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when a seeded product is inconsistent.</exception>
    public InMemoryProductCatalog()
    {
        foreach (var product in Seed)
        {
            product.Validate();
        }

        _products = Seed.ToDictionary(product => product.Id);
    }

    /// <inheritdoc />
    public IReadOnlyCollection<Product> Products => _products.Values;

    /// <inheritdoc />
    public bool TryGetProduct(Guid id, out Product product)
    {
        if (_products.TryGetValue(id, out var found))
        {
            product = found;
            return true;
        }

        product = null!;
        return false;
    }
}
