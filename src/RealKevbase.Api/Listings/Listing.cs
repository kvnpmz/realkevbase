namespace RealKevbase.Api.Listings;

/// <summary>
/// A single row from the PDF generator's project database: one produced PDF, keyed by the
/// identifier that also appears in the product's blob name.
/// </summary>
public sealed record Listing
{
    /// <summary>
    /// Gets the listing identifier, for example <c>first_apartment</c>.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the display title for the listing. Unique across the catalog.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Gets the configured output file name for the listing, when the generator recorded one.
    /// </summary>
    public string? OutputName { get; init; }

    /// <summary>
    /// Gets the template the PDF was generated from, for example <c>apartment_guide</c>.
    /// </summary>
    public string? Template { get; init; }

    /// <summary>
    /// Gets the date the PDF was completed, as recorded by the generator (ISO 8601).
    /// </summary>
    public string? CompletedDate { get; init; }

    /// <summary>
    /// Gets a value indicating whether the listing was generated with an editable text layer.
    /// </summary>
    public bool IsEditable { get; init; }
}
