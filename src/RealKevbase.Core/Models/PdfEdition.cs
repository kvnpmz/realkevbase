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
    /// </summary>    /// <param name="pdfBlobName">
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

    /// <summary>
    /// Derives the blob name of the full-resolution page image that ships beside a PDF.
    /// </summary>
    /// <remarks>
    /// The generator writes the page render into an <c>images/</c> folder next to the PDF
    /// (<c>guides/foo.pdf</c> and <c>guides/images/foo.png</c>), and the storefront cover is
    /// a downscale of that same file. Passing an A4 edition's blob name therefore locates
    /// its own image (<c>guides/images/foo-a4.png</c>), because the edition suffix is kept.
    /// </remarks>
    /// <param name="pdfBlobName">
    /// Blob name of the PDF, for example <c>guides/foo.pdf</c> or <c>guides/foo-a4.pdf</c>.
    /// </param>
    /// <returns>
    /// For example <c>guides/images/foo.png</c>, or <see langword="null"/> when
    /// <paramref name="pdfBlobName"/> is null, empty, whitespace or does not name a PDF.
    /// </returns>
    public static string? ImageBlobNameFor(string? pdfBlobName)
    {
        if (string.IsNullOrWhiteSpace(pdfBlobName))
        {
            return null;
        }

        var normalized = pdfBlobName.Replace('\\', '/');
        if (!normalized.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var stem = normalized[..^".pdf".Length];
        var lastSlash = stem.LastIndexOf('/');
        var folder = lastSlash < 0 ? string.Empty : stem[..(lastSlash + 1)];
        var file = lastSlash < 0 ? stem : stem[(lastSlash + 1)..];

        return file.Length == 0
            ? null
            : $"{folder}{ImageFolderName}/{file}{ImageExtension}";
    }

    /// <summary>
    /// Folder, relative to the PDF's own folder, that holds the page artwork.
    /// </summary>
    public const string ImageFolderName = "images";

    /// <summary>
    /// Extension of the full-resolution page image.
    /// </summary>
    public const string ImageExtension = ".png";
}
