namespace RealKevbase.Api.Auth;

/// <summary>
/// Options for the API's own token endpoint and bearer-token validation.
/// </summary>
/// <remarks>
/// Every value here is supplied by configuration (App Service settings or user secrets) and
/// never by source. When <see cref="Enabled"/> is <see langword="false"/> the API stays
/// anonymous, which is the shipped default: the storefront has no user accounts and must be
/// able to read the catalog without credentials.
/// </remarks>
public sealed class ApiTokenOptions
{
    /// <summary>
    /// Configuration section that holds these options.
    /// </summary>
    public const string SectionName = "ApiAuth";

    /// <summary>
    /// Gets or sets a value indicating whether the catalog and checkout routes require a bearer token.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the client identifier the token endpoint accepts.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the client secret the token endpoint accepts.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the HMAC key used to sign and validate tokens; at least 32 bytes.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the <c>iss</c> claim written into issued tokens.
    /// </summary>
    public string Issuer { get; set; } = "realkevbase-api";

    /// <summary>
    /// Gets or sets the <c>aud</c> claim written into issued tokens.
    /// </summary>
    public string Audience { get; set; } = "realkevbase-web";

    /// <summary>
    /// Gets or sets the lifetime of an issued token, in minutes.
    /// </summary>
    public int TokenLifetimeMinutes { get; set; } = 60;
}
