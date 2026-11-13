namespace RealKevbase.Core.Models;

/// <summary>
/// One paper edition of a digital guide: the file a buyer downloads, and the label the
/// storefront uses for it.
/// </summary>
/// <param name="Label">Customer-facing paper size, for example <c>US Letter</c>.</param>
/// <param name="BlobName">Name of the PDF blob inside the configured container.</param>
public sealed record PdfEdition(string Label, string BlobName)
{
    /// <summary>
    /// Label of the edition every digital guide ships with.
    /// </summary>
    public const string LetterLabel = "US Letter";

    /// <summary>
    /// Label of the optional A4 edition that older listings were rendered with.
    /// </summary>
    public const string A4Label = "A4";

    /// <summary>
    /// Marker that distinguishes the A4 edition from the Letter PDF it sits beside.
    /// </summary>
    public const string A4BlobSuffix = "-a4";

    /// <summary>
    /// Derives the blob name of the A4 edition that belongs to a Letter PDF.
    /// </summary>
    /// <param name="pdfBlobName">
    /// Blob name of the Letter PDF, for example <c>guides/foo.pdf</c>.
    /// </param>
    /// <returns>
    /// <c>guides/foo-a4.pdf</c>, or <see langword="null"/> when <paramref name="pdfBlobName"/>
    /// is null, empty or whitespace.
    /// </returns>
    public static string? A4BlobNameFor(string? pdfBlobName)
    {
        if (string.IsNullOrWhiteSpace(pdfBlobName))
        {
            return null;
        }

        var normalized = pdfBlobName.Replace('\\', '/');
        var stem = normalized.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^".pdf".Length]
            : normalized;

        return $"{stem}{A4BlobSuffix}.pdf";
    }
}
