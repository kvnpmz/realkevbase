using System.Collections.Concurrent;

using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

using Microsoft.Extensions.Logging;

using RealKevbase.Core.Models;

namespace RealKevbase.Core.Services;

/// <summary>
/// A product thumbnail read from blob storage, together with the metadata needed to
/// serve it without another round trip.
/// </summary>
/// <param name="Content">Stream positioned at the start of the image bytes.</param>
/// <param name="ContentType">MIME type recorded on the blob, for example <c>image/webp</c>.</param>
/// <param name="ETag">Validator for conditional requests, quoted as blob storage returns it.</param>
public sealed record ProductImage(Stream Content, string ContentType, string ETag);

/// <summary>
/// Reads the artwork that illustrates a digital product.
/// </summary>
/// <remarks>
/// Thumbnails live beside the PDFs they illustrate, under an <c>images/</c> folder, so a
/// product and its artwork ship together. The container stays private and the blob name is
/// never returned to the browser: callers stream the bytes through this service instead.
/// </remarks>
public interface IProductImageService
{
    /// <summary>
    /// Number of gallery slots a product can expose, in display order.
    /// </summary>
    /// <remarks>
    /// Slot 0 is the same artwork the storefront uses as the card thumbnail; slots 1 to 3 are
    /// the detail zoom, the tablet mockup and the A4 preview that the generator renders beside
    /// it. Slots 4 and 5 are the two pages every listing shares — how to use the files, and
    /// the digital-download notice — stored once instead of once per product. Not every
    /// listing has all six, so callers must treat a missing slot as normal.
    /// </remarks>
    const int GallerySlotCount = 6;

    /// <summary>
    /// Opens the thumbnail for a digital product.
    /// </summary>
    /// <param name="product">Product whose artwork should be read.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>
    /// The image, or <see langword="null"/> when the product has no PDF, has no artwork, or
    /// the artwork has not been published to storage yet.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="product"/> is <see langword="null"/>.</exception>
    Task<ProductImage?> OpenThumbnailAsync(Product product, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens one image of a digital product's gallery.
    /// </summary>
    /// <param name="product">Product whose artwork should be read.</param>
    /// <param name="slot">Gallery slot, from 0 to <see cref="GallerySlotCount"/> - 1.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>
    /// The image, or <see langword="null"/> when the slot is out of range, the product has no
    /// PDF, or that render was never published. A missing render is a normal state: the
    /// gallery shows the ones that exist.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="product"/> is <see langword="null"/>.</exception>
    Task<ProductImage?> OpenGalleryImageAsync(
        Product product,
        int slot,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports what storage actually holds for each product.
    /// </summary>
    /// <remarks>
    /// One cached folder listing answers this for the whole catalog, so the storefront can build
    /// only the image URLs that exist and can state the paper sizes the listing really covers,
    /// instead of requesting every slot and dropping the 404s.
    /// </remarks>
    /// <param name="products">Products to describe; merchandise and products without a PDF are skipped.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>
    /// Product id to its published artwork, or <see langword="null"/> when storage could not be
    /// listed — callers should then treat every slot as unknown rather than as missing.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="products"/> is <see langword="null"/>.</exception>
    Task<IReadOnlyDictionary<Guid, PublishedArtwork>?> DescribeArtworkAsync(
        IReadOnlyList<Product> products,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// What a listing has actually published to storage.
/// </summary>
/// <param name="GallerySlots">Gallery slots that can be served, in display order.</param>
/// <param name="PageSizes">Paper sizes the listing is laid out for, always starting with US Letter.</param>
public sealed record PublishedArtwork(IReadOnlyList<int> GallerySlots, IReadOnlyList<string> PageSizes);

/// <summary>
/// Reads product thumbnails from the same private container that holds the PDFs.
/// </summary>
/// <remarks>
/// The blob name is derived from <see cref="Product.PdfBlobName"/>, so the generator's layout
/// (<c>guides/foo.pdf</c> with artwork at <c>guides/images/foo.webp</c>) is the only convention
/// that has to be maintained. Nothing here is signed: the bytes are proxied by the API, which
/// keeps the storage account private and avoids handing out URLs that outlive the request.
/// </remarks>
public sealed class AzureBlobProductImageService : IProductImageService
{
    /// <summary>
    /// Folder, relative to the PDF's own folder, that holds the artwork.
    /// </summary>
    public const string ThumbnailFolderName = "images";

    /// <summary>
    /// File extension of the published thumbnails.
    /// </summary>
    public const string ThumbnailExtension = ".webp";

    /// <summary>
    /// File-name suffixes of the gallery renders, in the order the storefront shows them.
    /// Mirrors the variants the PDF generator writes beside each page.
    /// </summary>
    private static readonly string[] GallerySuffixes = ["", "-zoom", "-ipad", "-a4"];

    /// <summary>
    /// Folder inside the artwork folder that holds the pages every listing shares.
    /// </summary>
    public const string SharedFolderName = "shared";

    /// <summary>
    /// Shared pages that follow each listing's own renders, in slot order.
    /// </summary>
    private static readonly string[] SharedGalleryFiles = ["guide.webp", "disclaimer.webp"];

    /// <summary>
    /// Extension of the deliverable documents.
    /// </summary>
    private const string PdfExtension = ".pdf";

    /// <summary>
    /// Position of the A4 preview in <see cref="GallerySuffixes"/>.
    /// </summary>
    private const int A4PreviewSlot = 3;

    /// <summary>
    /// Paper size every listing is laid out for.
    /// </summary>
    private const string LetterPageSize = "US Letter";

    /// <summary>
    /// Paper size of the optional second edition.
    /// </summary>
    private const string A4PageSize = "A4";

    /// <summary>
    /// One folder listing, kept until <see cref="IndexLifetime"/> runs out.
    /// </summary>
    /// <param name="Names">Published blob names under the folder.</param>
    /// <param name="ExpiresAt">Moment the listing should be read again.</param>
    private sealed record PublishedNames(IReadOnlySet<string> Names, DateTimeOffset ExpiresAt);

    /// <summary>
    /// How long one folder listing is reused before storage is asked again.
    /// </summary>
    /// <remarks>
    /// Artwork only changes when a listing is published, so a long window keeps the catalog
    /// endpoint free of storage calls while a new listing still appears within minutes.
    /// </remarks>
    private static readonly TimeSpan IndexLifetime = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How long a failed listing is remembered, so an unavailable storage account cannot turn
    /// every catalog request into another attempt with the SDK's own retries on top.
    /// </summary>
    private static readonly TimeSpan FailureBackoff = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Ceiling on one listing call, so a storage outage costs the catalog a few seconds at most
    /// rather than the SDK's full retry window.
    /// </summary>
    private static readonly TimeSpan ListingTimeout = TimeSpan.FromSeconds(3);

    private readonly BlobContainerClient _containerClient;

    private readonly ILogger<AzureBlobProductImageService> _logger;

    /// <summary>
    /// Published blob names per artwork folder, replaced as a whole when refreshed.
    /// </summary>
    private readonly ConcurrentDictionary<string, PublishedNames> _published = new(StringComparer.Ordinal);

    /// <summary>
    /// Folder name to the moment listing it may be attempted again.
    /// </summary>
    private readonly ConcurrentDictionary<string, DateTimeOffset> _retryAfter = new(StringComparer.Ordinal);

    /// <summary>
    /// Serialises refreshes, so a burst of catalog requests triggers one listing at most.
    /// </summary>
    private readonly SemaphoreSlim _listingGate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureBlobProductImageService"/> class.
    /// </summary>
    /// <param name="blobServiceClient">Client for the storage account.</param>
    /// <param name="options">Container configuration shared with the PDF storage service.</param>
    /// <param name="logger">Logger for storage failures that only degrade the gallery.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null"/>.</exception>
    public AzureBlobProductImageService(
        BlobServiceClient blobServiceClient,
        AzurePdfStorageOptions options,
        ILogger<AzureBlobProductImageService> logger)
    {
        ArgumentNullException.ThrowIfNull(blobServiceClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _containerClient = blobServiceClient.GetBlobContainerClient(options.PdfContainerName);
        _logger = logger;
    }

    /// <summary>
    /// Derives the thumbnail blob name for a product.
    /// </summary>
    /// <param name="product">Product whose artwork location should be computed.</param>
    /// <returns>
    /// The blob name, for example <c>guides/images/foo.webp</c> for a PDF at
    /// <c>guides/foo.pdf</c>, or <see langword="null"/> when the product has no PDF blob.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="product"/> is <see langword="null"/>.</exception>
    public static string? GetThumbnailBlobName(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);

        if (string.IsNullOrWhiteSpace(product.PdfBlobName))
        {
            return null;
        }

        // Blob names always use forward slashes, whatever the host platform separator is.
        var blobName = product.PdfBlobName.Replace('\\', '/');
        var lastSlash = blobName.LastIndexOf('/');
        var folder = lastSlash >= 0 ? blobName[..lastSlash] : string.Empty;
        var file = lastSlash >= 0 ? blobName[(lastSlash + 1)..] : blobName;
        var stem = Path.GetFileNameWithoutExtension(file);

        if (string.IsNullOrEmpty(stem))
        {
            return null;
        }

        var thumbnail = $"{stem}{ThumbnailExtension}";

        return string.IsNullOrEmpty(folder)
            ? $"{ThumbnailFolderName}/{thumbnail}"
            : $"{folder}/{ThumbnailFolderName}/{thumbnail}";
    }

    /// <summary>
    /// Derives the gallery blob name for a product and slot.
    /// </summary>
    /// <param name="product">Product whose artwork location should be computed.</param>
    /// <param name="slot">Gallery slot, from 0 to <see cref="IProductImageService.GallerySlotCount"/> - 1.</param>
    /// <returns>
    /// The blob name, for example <c>guides/images/foo-zoom.webp</c> for slot 1, or
    /// <see langword="null"/> when the slot is out of range or the product has no PDF blob.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="product"/> is <see langword="null"/>.</exception>
    public static string? GetGalleryBlobName(Product product, int slot)
    {
        ArgumentNullException.ThrowIfNull(product);

        if (slot < 0 || slot >= IProductImageService.GallerySlotCount)
        {
            return null;
        }

        var thumbnail = GetThumbnailBlobName(product);
        if (thumbnail is null)
        {
            return null;
        }

        var folder = thumbnail[..(thumbnail.LastIndexOf('/') + 1)];

        // Slots past the per-listing renders are the pages shared by every listing: one copy
        // in the artwork folder serves the whole catalog.
        if (slot >= GallerySuffixes.Length)
        {
            return $"{folder}{SharedFolderName}/{SharedGalleryFiles[slot - GallerySuffixes.Length]}";
        }

        // Thumbnail names always end in the published image extension, so the suffix can be
        // inserted in front of it without re-deriving the folder and stem.
        var stem = thumbnail[..^ThumbnailExtension.Length];
        return $"{stem}{GallerySuffixes[slot]}{ThumbnailExtension}";
    }

    /// <inheritdoc />
    public Task<ProductImage?> OpenThumbnailAsync(Product product, CancellationToken cancellationToken = default)
    {
        var blobName = GetThumbnailBlobName(product);
        return blobName is null ? Task.FromResult<ProductImage?>(null) : OpenAsync(blobName, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ProductImage?> OpenGalleryImageAsync(
        Product product,
        int slot,
        CancellationToken cancellationToken = default)
    {
        var blobName = GetGalleryBlobName(product, slot);
        return blobName is null ? Task.FromResult<ProductImage?>(null) : OpenAsync(blobName, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, PublishedArtwork>?> DescribeArtworkAsync(
        IReadOnlyList<Product> products,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(products);

        var digital = products
            .Where(product => product.IsDigitalPdf && GetThumbnailBlobName(product) is not null)
            .ToArray();

        // Every listing publishes into the folder that holds its PDFs, so this is normally a
        // single listing call for the whole catalog.
        var folders = digital
            .Select(product => GetPdfFolderName(product)!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var published = new Dictionary<string, IReadOnlySet<string>>(folders.Length, StringComparer.Ordinal);
        foreach (var folder in folders)
        {
            var names = await GetPublishedNamesAsync(folder, cancellationToken).ConfigureAwait(false);
            if (names is null)
            {
                // Unknown beats wrong: the storefront falls back to asking slot by slot.
                return null;
            }

            published[folder] = names;
        }

        var artwork = new Dictionary<Guid, PublishedArtwork>(digital.Length);
        foreach (var product in digital)
        {
            var names = published[GetPdfFolderName(product)!];

            var pageSizes = GetPublishedPageSizes(product, names);

            var slots = Enumerable
                .Range(0, IProductImageService.GallerySlotCount)
                .Where(slot => names.Contains(GetGalleryBlobName(product, slot)!))
                // The A4 preview illustrates an edition that is only offered once it is
                // published, so it is withheld while the Letter PDF is all a buyer receives.
                .Where(slot => slot != A4PreviewSlot || pageSizes.Contains(A4PageSize))
                .ToArray();

            artwork[product.Id] = new PublishedArtwork(slots, pageSizes);
        }

        return artwork;
    }

    /// <summary>
    /// Reports the paper sizes a listing can actually be printed at.
    /// </summary>
    /// <param name="product">Product whose editions should be checked.</param>
    /// <param name="names">Published blob names under the product's folder.</param>
    /// <returns>
    /// <c>US Letter</c> for every listing, plus <c>A4</c> only when the A4 edition is published
    /// beside the Letter PDF — the extra version is never claimed from its preview alone.
    /// </returns>
    private static IReadOnlyList<string> GetPublishedPageSizes(Product product, IReadOnlySet<string> names)
    {
        var a4 = GetA4EditionBlobName(product);
        return a4 is not null && names.Contains(a4) ? [LetterPageSize, A4PageSize] : [LetterPageSize];
    }

    /// <summary>
    /// Derives the blob name of a listing's A4 edition.
    /// </summary>
    /// <param name="product">Product whose A4 edition should be located.</param>
    /// <returns>
    /// For example <c>guides/foo-a4.pdf</c> beside <c>guides/foo.pdf</c>, or <see langword="null"/>
    /// when the product has no PDF blob.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="product"/> is <see langword="null"/>.</exception>
    public static string? GetA4EditionBlobName(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);

        return PdfEdition.A4BlobNameFor(product.PdfBlobName);
    }

    /// <summary>
    /// Derives the folder a product publishes into, holding both its PDFs and its artwork.
    /// </summary>
    /// <param name="product">Product whose publication folder should be computed.</param>
    /// <returns>
    /// A blob-name prefix ending in a slash, for example <c>guides/</c>, or
    /// <see langword="null"/> when the product has no PDF blob.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="product"/> is <see langword="null"/>.</exception>
    public static string? GetPdfFolderName(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);

        var pdf = product.PdfBlobName?.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(pdf))
        {
            return null;
        }

        var lastSlash = pdf.LastIndexOf('/');
        return lastSlash < 0 ? string.Empty : pdf[..(lastSlash + 1)];
    }

    /// <summary>
    /// Lists the blobs under one publication folder, reusing the previous answer while it is fresh.
    /// </summary>
    /// <param name="folder">Blob-name prefix ending in a slash.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The published blob names, or <see langword="null"/> when listing failed.</returns>
    private async Task<IReadOnlySet<string>?> GetPublishedNamesAsync(
        string folder,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (_published.TryGetValue(folder, out var cached) && cached.ExpiresAt > now)
        {
            return cached.Names;
        }

        if (_retryAfter.TryGetValue(folder, out var retryAt) && retryAt > now)
        {
            return null;
        }

        await _listingGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            now = DateTimeOffset.UtcNow;
            if (_published.TryGetValue(folder, out cached) && cached.ExpiresAt > now)
            {
                return cached.Names;
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(ListingTimeout);

                await foreach (var blob in _containerClient
                    .GetBlobsAsync(BlobTraits.None, BlobStates.None, folder, timeout.Token)
                    .ConfigureAwait(false))
                {
                    names.Add(blob.Name);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException
                                              || !cancellationToken.IsCancellationRequested)
            {
                // Storage being unreachable must not take the catalog down: report "unknown",
                // give up quickly, and try again after a short pause.
                _retryAfter[folder] = DateTimeOffset.UtcNow.Add(FailureBackoff);
                _logger.LogWarning(
                    exception,
                    "Could not list published artwork under {Folder}; the catalog will leave gallery slots unknown for a moment.",
                    folder);
                return null;
            }

            _retryAfter.TryRemove(folder, out _);
            _published[folder] = new PublishedNames(names, DateTimeOffset.UtcNow.Add(IndexLifetime));
            return names;
        }
        finally
        {
            _listingGate.Release();
        }
    }

    /// <summary>
    /// Streams an image blob.
    /// </summary>
    /// <param name="blobName">Blob name inside the shared container.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The image, or <see langword="null"/> when the blob does not exist.</returns>
    private async Task<ProductImage?> OpenAsync(string blobName, CancellationToken cancellationToken)
    {
        var blob = _containerClient.GetBlobClient(blobName);

        try
        {
            // One round trip, not two: the download itself reports a missing blob, so no
            // separate ExistsAsync probe is needed. A gallery view would otherwise double
            // the storage calls made per detail page.
            var download = await blob
                .DownloadStreamingAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var result = download.Value;

            return new ProductImage(
                result.Content,
                string.IsNullOrWhiteSpace(result.Details.ContentType) ? "image/webp" : result.Details.ContentType,
                result.Details.ETag.ToString());
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            // A render the listing never produced is a normal state for gallery slots.
            return null;
        }
    }
}
