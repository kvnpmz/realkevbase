using Azure.Communication.Email;
using Microsoft.Extensions.Configuration;

namespace RealKevbase.Core.Services;

/// <summary>
/// Configuration for sending download links through Azure Communication Services Email,
/// read from the <c>AzureEmail</c> configuration section.
/// </summary>
/// <remarks>
/// Do not hardcode secrets. Supply <see cref="ConnectionString"/> through environment
/// variables (<c>AzureEmail__ConnectionString</c>), user secrets or a key vault.
/// <see cref="SenderAddress"/> must belong to a domain verified in the Email Communication
/// Service resource, otherwise Azure rejects the send with <c>DomainNotVerified</c>.
/// </remarks>
public sealed class AzureEmailOptions
{
    /// <summary>
    /// The configuration section these options are read from.
    /// </summary>
    public const string SectionName = "AzureEmail";

    /// <summary>
    /// Gets or sets the Azure Communication Services connection string used to authenticate
    /// the email client.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the sender address, for example <c>noreply@realkevbase.com</c>.
    /// </summary>
    public string? SenderAddress { get; set; }

    /// <summary>
    /// Gets a value indicating whether enough settings are present to send email.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ConnectionString) && !string.IsNullOrWhiteSpace(SenderAddress);

    /// <summary>
    /// Binds the <c>AzureEmail</c> section, also accepting the connection string from
    /// <c>ConnectionStrings:AzureEmail</c> so either App Service form works.
    /// </summary>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The bound options.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration"/> is <see langword="null"/>.</exception>
    public static AzureEmailOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(SectionName);

        return new AzureEmailOptions
        {
            ConnectionString = section[nameof(ConnectionString)]
                ?? configuration.GetConnectionString("AzureEmail"),
            SenderAddress = section[nameof(SenderAddress)]
        };
    }

    /// <summary>
    /// Creates the Azure Communication Services email client for these options.
    /// </summary>
    /// <returns>A new <see cref="EmailClient"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown when <see cref="ConnectionString"/> is missing.</exception>
    public EmailClient CreateEmailClient()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new InvalidOperationException(
                $"Azure Communication Services Email is not configured. Set " +
                $"'{SectionName}:{nameof(ConnectionString)}' through configuration, environment variables or user secrets.");
        }

        return new EmailClient(ConnectionString);
    }
}
