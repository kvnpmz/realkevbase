using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RealKevbase.Core.Models;

namespace RealKevbase.Core.Services;

/// <summary>
/// Configuration for <see cref="AzureBlobPdfStorageService"/>, bound from the
/// <c>AzureStorage</c> configuration section.
/// </summary>
/// <remarks>
/// Do not hardcode secrets. Supply <see cref="ConnectionString"/> through environment
/// variables (<c>AzureStorage__ConnectionString</c>), user secrets or a key vault; or leave it
/// empty and supply <see cref="AccountName"/> to authenticate with a managed identity.
/// </remarks>
public sealed class AzurePdfStorageOptions
{
    /// <summary>
    /// The configuration section these options are read from.
    /// </summary>
    public const string SectionName = "AzureStorage";

    /// <summary>
    /// Gets or sets the Azure Storage connection string. When set, SAS tokens are signed with
    /// the account key it contains.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the storage account name used for keyless authentication via
    /// <see cref="DefaultAzureCredential"/>. Ignored when <see cref="ConnectionString"/> is set.
    /// </summary>
    public string? AccountName { get; set; }

    /// <summary>
    /// Gets or sets the container that holds the PDF blobs. Defaults to <c>pdfs</c>.
    /// </summary>
    public string PdfContainerName { get; set; } = "pdfs";
}

/// <summary>
/// Azure Blob Storage implementation of <see cref="IPdfStorageService"/> that hands out
/// read-only SAS URLs valid for 24 hours.
/// </summary>
public sealed class AzureBlobPdfStorageService : IPdfStorageService
{
    /// <summary>
    /// The fixed lifetime of every issued download URL: 24 hours.
    /// </summary>
    public static readonly TimeSpan UrlLifetime = TimeSpan.FromHours(24);

    // Small backdated start so clock skew between this server and Azure does not reject the link.
    private static readonly TimeSpan SasClockSkew = TimeSpan.FromMinutes(5);

    private readonly BlobServiceClient _blobServiceClient;
    private readonly BlobContainerClient _containerClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureBlobPdfStorageService"/> class.
    /// </summary>
    /// <param name="blobServiceClient">Client for the storage account.</param>
    /// <param name="options">Container and credential configuration.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when no PDF container is configured.</exception>
    public AzureBlobPdfStorageService(BlobServiceClient blobServiceClient, AzurePdfStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(blobServiceClient);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.PdfContainerName))
        {
            throw new ArgumentException(
                $"{AzurePdfStorageOptions.SectionName}:{nameof(AzurePdfStorageOptions.PdfContainerName)} must be configured.",
                nameof(options));
        }

        _blobServiceClient = blobServiceClient;
        _containerClient = blobServiceClient.GetBlobContainerClient(options.PdfContainerName);
    }

    /// <inheritdoc />
    public TimeSpan LinkLifetime => UrlLifetime;

    /// <inheritdoc />
    public Task<Uri> CreateDownloadUrlAsync(Product product, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(product);
        product.Validate();

        if (!product.IsDigitalPdf)
        {
            throw new InvalidOperationException(
                $"Product '{product.Id}' is fulfilled as {product.Fulfillment} and has no PDF download.");
        }

        return CreateDownloadUrlAsync(product.PdfBlobName!, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Uri> CreateDownloadUrlAsync(string blobName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobName);

        var blob = _containerClient.GetBlobClient(blobName);

        // A signed URL is valid even when the blob is missing, which hands the buyer a link
        // that 404s. Fail loudly instead: a missing blob is an operational mistake (wrong
        // container, or an upload that never happened), not something the buyer can resolve.
        if (!await blob.ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"Blob '{blobName}' does not exist in container '{_containerClient.Name}'. " +
                "Upload the product PDF before selling it.");
        }

        var startsOn = DateTimeOffset.UtcNow.Subtract(SasClockSkew);
        var expiresOn = DateTimeOffset.UtcNow.Add(LinkLifetime);

        var sasBuilder = new BlobSasBuilder(BlobSasPermissions.Read, expiresOn)
        {
            BlobContainerName = _containerClient.Name,
            BlobName = blob.Name,
            Resource = "b",
            StartsOn = startsOn
        };

        // Connection string / account key: the blob client can sign the SAS itself.
        if (blob.CanGenerateSasUri)
        {
            return blob.GenerateSasUri(sasBuilder);
        }

        // Managed identity / Entra ID: sign with a user delegation key instead of an account key.
        var delegationKey = await _blobServiceClient
            .GetUserDelegationKeyAsync(startsOn, expiresOn, cancellationToken)
            .ConfigureAwait(false);

        var sasParameters = sasBuilder.ToSasQueryParameters(delegationKey, _blobServiceClient.AccountName);
        return new UriBuilder(blob.Uri) { Query = sasParameters.ToString() }.Uri;
    }
}

/// <summary>
/// Registration helpers for <see cref="IPdfStorageService"/>.
/// </summary>
public static class PdfStorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="AzureBlobPdfStorageService"/> as the <see cref="IPdfStorageService"/>
    /// using the <c>AzureStorage</c> configuration section.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">Application configuration containing the Azure storage settings.</param>
    /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null"/>.</exception>
    public static IServiceCollection AddAzureBlobPdfStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(AzurePdfStorageOptions.SectionName);
        var options = new AzurePdfStorageOptions
        {
            // App Service commonly stores this as a connection string named "AzureStorage"
            // (ConnectionStrings__AzureStorage); accept both forms.
            ConnectionString = section[nameof(AzurePdfStorageOptions.ConnectionString)]
                ?? configuration.GetConnectionString("AzureStorage"),
            AccountName = section[nameof(AzurePdfStorageOptions.AccountName)],
            PdfContainerName = section[nameof(AzurePdfStorageOptions.PdfContainerName)] is { Length: > 0 } container
                ? container
                : "pdfs"
        };

        services.AddSingleton(options);
        services.AddSingleton(_ => CreateBlobServiceClient(options));
        services.AddSingleton<IPdfStorageService, AzureBlobPdfStorageService>();

        return services;
    }

    private static BlobServiceClient CreateBlobServiceClient(AzurePdfStorageOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return new BlobServiceClient(options.ConnectionString);
        }

        if (!string.IsNullOrWhiteSpace(options.AccountName))
        {
            return new BlobServiceClient(
                new Uri($"https://{options.AccountName}.blob.core.windows.net"),
                new DefaultAzureCredential());
        }

        throw new InvalidOperationException(
            $"Azure Blob Storage is not configured. Set '{AzurePdfStorageOptions.SectionName}:{nameof(AzurePdfStorageOptions.ConnectionString)}' " +
            $"or '{AzurePdfStorageOptions.SectionName}:{nameof(AzurePdfStorageOptions.AccountName)}' " +
            "through configuration, environment variables or user secrets.");
    }
}
