using System.Globalization;
using System.Net;
using System.Text;

using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Logging;

using RealKevbase.Core.Models;

namespace RealKevbase.Core.Services;

/// <summary>
/// Delivers issued PDF download links to the buyer by email through Azure Communication
/// Services.
/// </summary>
/// <remarks>
/// When <see cref="AzureEmailOptions.IsConfigured"/> is <see langword="false"/> the service
/// degrades to logging that links were issued, so a deployment without email infrastructure
/// still fulfils orders through the checkout success page. Pre-signed URLs are never written
/// to logs because they are bearer credentials.
/// </remarks>
public sealed class LoggingPdfDeliveryService : IPdfDeliveryService
{
    private readonly ILogger<LoggingPdfDeliveryService> _logger;
    private readonly AzureEmailOptions _options;
    private readonly EmailClient? _emailClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoggingPdfDeliveryService"/> class.
    /// </summary>
    /// <param name="logger">Logger used to record issuance and delivery outcomes.</param>
    /// <param name="options">Azure Communication Services Email settings.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null"/>.</exception>
    public LoggingPdfDeliveryService(ILogger<LoggingPdfDeliveryService> logger, AzureEmailOptions options)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);

        _logger = logger;
        _options = options;
        _emailClient = options.IsConfigured ? options.CreateEmailClient() : null;

        if (_emailClient is null)
        {
            _logger.LogWarning(
                "Azure Communication Services Email is not configured ('{Section}:{ConnectionString}' or '{Section}:{SenderAddress}' missing); " +
                "download links will be logged instead of emailed.",
                AzureEmailOptions.SectionName,
                nameof(AzureEmailOptions.ConnectionString),
                AzureEmailOptions.SectionName,
                nameof(AzureEmailOptions.SenderAddress));
        }
    }

    /// <inheritdoc />
    public async Task DeliverAsync(PdfDelivery delivery, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        if (_emailClient is null)
        {
            _logger.LogWarning(
                "Issued {DownloadCount} PDF download link(s) for Stripe session {SessionId}, but no delivery provider is configured; no email was sent (buyer email present: {HasBuyerEmail}).",
                delivery.Downloads.Count,
                delivery.SessionId,
                !string.IsNullOrWhiteSpace(delivery.CustomerEmail));

            return;
        }

        if (delivery.Downloads.Count == 0)
        {
            _logger.LogInformation(
                "Stripe session {SessionId} has no digital downloads to email.",
                delivery.SessionId);

            return;
        }

        if (string.IsNullOrWhiteSpace(delivery.CustomerEmail))
        {
            _logger.LogWarning(
                "Stripe session {SessionId} produced {DownloadCount} download link(s) but no buyer email; the buyer can still download from the success page.",
                delivery.SessionId,
                delivery.Downloads.Count);

            return;
        }

        var message = BuildMessage(delivery);

        try
        {
            var operation = await _emailClient
                .SendAsync(WaitUntil.Completed, message, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Emailed {DownloadCount} PDF download link(s) for Stripe session {SessionId} to the buyer (operation status {Status}).",
                delivery.Downloads.Count,
                delivery.SessionId,
                operation.Value.Status);
        }
        catch (RequestFailedException exception)
        {
            // Surface the Azure error code: 'DomainNotVerified' and 'SenderNotAuthenticated'
            // mean the DNS verification or the sender address needs attention, not a retry.
            _logger.LogError(
                exception,
                "Azure Communication Services rejected the download email for Stripe session {SessionId} (error code {ErrorCode}).",
                delivery.SessionId,
                exception.ErrorCode);

            throw;
        }
    }

    private EmailMessage BuildMessage(PdfDelivery delivery)
    {
        var subject = delivery.Downloads.Count == 1
            ? "Your RealKevbase PDF is ready (link expires in 24 hours)"
            : $"Your {delivery.Downloads.Count} RealKevbase PDFs are ready (links expire in 24 hours)";

        var content = new EmailContent(subject)
        {
            PlainText = BuildPlainText(delivery),
            Html = BuildHtml(delivery)
        };

        return new EmailMessage(
            _options.SenderAddress!,
            new EmailRecipients([new EmailAddress(delivery.CustomerEmail!)]),
            content);
    }

    private static string BuildHtml(PdfDelivery delivery)
    {
        var builder = new StringBuilder();

        builder.Append("<!doctype html><html><body style=\"margin:0;padding:24px;background:#f4f5f7;")
            .Append("font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;color:#111827;\">")
            .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\"><tr><td align=\"center\">")
            .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" ")
            .Append("style=\"max-width:540px;background:#ffffff;border-radius:14px;padding:28px;\"><tr><td>")
            .Append("<p style=\"margin:0 0 6px;font-size:12px;letter-spacing:.08em;text-transform:uppercase;color:#6b7280;\">RealKevbase</p>")
            .Append("<h1 style=\"margin:0 0 10px;font-size:22px;line-height:1.3;\">Your downloads are ready</h1>")
            .Append("<p style=\"margin:0 0 22px;font-size:14px;color:#4b5563;\">Thanks for your order. ")
            .Append("Each link is private to this order and expires 24 hours after checkout.</p>");

        foreach (var download in delivery.Downloads)
        {
            builder.Append("<p style=\"margin:0 0 8px;font-size:15px;font-weight:600;\">")
                .Append(Html(download.Product.Name))
                .Append("</p>")
                .Append("<p style=\"margin:0 0 6px;\"><a href=\"")
                .Append(Html(download.Url.AbsoluteUri))
                .Append("\" style=\"display:inline-block;padding:11px 20px;border-radius:8px;background:#1d4ed8;color:#ffffff;")
                .Append("text-decoration:none;font-size:14px;font-weight:600;\">Download PDF</a></p>")
                .Append("<p style=\"margin:0 0 20px;font-size:12px;color:#6b7280;\">Link valid until ")
                .Append(Html(FormatExpiry(download.ExpiresAt)))
                .Append("</p>");
        }

        builder.Append("<p style=\"margin:0;padding-top:18px;border-top:1px solid #e5e7eb;font-size:12px;color:#6b7280;\">")
            .Append("Links expire in 24 hours. If they have lapsed, open the receipt page from your Stripe confirmation email ")
            .Append("and fresh links are issued automatically.</p>")
            .Append("</td></tr></table></td></tr></table></body></html>");

        return builder.ToString();
    }

    private static string BuildPlainText(PdfDelivery delivery)
    {
        var builder = new StringBuilder();

        builder.AppendLine("Your RealKevbase downloads are ready.")
            .AppendLine()
            .AppendLine("Each link is private to this order and expires 24 hours after checkout.")
            .AppendLine();

        foreach (var download in delivery.Downloads)
        {
            builder.AppendLine(download.Product.Name)
                .AppendLine(download.Url.AbsoluteUri)
                .AppendLine($"Link valid until {FormatExpiry(download.ExpiresAt)}")
                .AppendLine();
        }

        builder.AppendLine("Links expire in 24 hours. If they have lapsed, open the receipt page from")
            .Append("your Stripe confirmation email and fresh links are issued automatically.");

        return builder.ToString();
    }

    private static string FormatExpiry(DateTimeOffset expiresAt) =>
        expiresAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    private static string Html(string value) => WebUtility.HtmlEncode(value);
}
