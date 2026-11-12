using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace RealKevbase.Api.Auth;

/// <summary>
/// A bearer token handed to a client that authenticated against the token endpoint.
/// </summary>
/// <param name="AccessToken">Signed JWT to send in <c>Authorization: Bearer</c>.</param>
/// <param name="TokenType">Token type, always <c>Bearer</c>.</param>
/// <param name="ExpiresIn">Lifetime in seconds, for OAuth-style clients.</param>
public sealed record IssuedToken(string AccessToken, string TokenType, int ExpiresIn);

/// <summary>
/// Issues and describes the tokens accepted by <c>AddJwtBearer</c>.
/// </summary>
/// <remarks>
/// The signing key is symmetric, so the API both mints and validates tokens and needs no
/// identity provider. Credentials are compared in constant time so a wrong secret cannot be
/// recovered by timing the response. This class never logs a secret or a token.
/// </remarks>
public sealed class ApiTokenService
{
    private readonly ApiTokenOptions _options;
    private readonly SigningCredentials? _signingCredentials;
    private readonly JwtSecurityTokenHandler _handler = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiTokenService"/> class.
    /// </summary>
    /// <param name="options">Token endpoint and signing configuration.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when authentication is enabled but the credentials or signing key are missing or too short.
    /// </exception>
    public ApiTokenService(IOptions<ApiTokenOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;

        if (!_options.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            throw new InvalidOperationException(
                "ApiAuth is enabled but ClientId or ClientSecret is missing. Set ApiAuth__ClientId and ApiAuth__ClientSecret.");
        }

        if (Encoding.UTF8.GetByteCount(_options.SigningKey) < 32)
        {
            throw new InvalidOperationException(
                "ApiAuth is enabled but SigningKey is missing or shorter than 32 bytes. Set ApiAuth__SigningKey.");
        }

        _signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            SecurityAlgorithms.HmacSha256);
    }

    /// <summary>
    /// Gets a value indicating whether the token endpoint is configured and enabled.
    /// </summary>
    public bool IsEnabled => _options.Enabled && _signingCredentials is not null;

    /// <summary>
    /// Exchanges client credentials for a signed access token.
    /// </summary>
    /// <param name="clientId">Client identifier presented by the caller.</param>
    /// <param name="clientSecret">Client secret presented by the caller.</param>
    /// <returns>The token, or <see langword="null"/> when the credentials do not match.</returns>
    public IssuedToken? Issue(string? clientId, string? clientSecret)
    {
        if (!IsEnabled)
        {
            return null;
        }

        if (!FixedTimeEquals(clientId, _options.ClientId) || !FixedTimeEquals(clientSecret, _options.ClientSecret))
        {
            return null;
        }

        var issuedAt = DateTimeOffset.UtcNow;
        var expiresAt = issuedAt.AddMinutes(Math.Max(1, _options.TokenLifetimeMinutes));

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, _options.ClientId),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
            ],
            notBefore: issuedAt.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: _signingCredentials);

        return new IssuedToken(
            _handler.WriteToken(token),
            "Bearer",
            (int)Math.Round((expiresAt - issuedAt).TotalSeconds));
    }

    /// <summary>
    /// Compares a presented value against the configured value in constant time.
    /// </summary>
    /// <param name="presented">Value supplied by the caller.</param>
    /// <param name="expected">Configured value.</param>
    /// <returns><see langword="true"/> when both values are byte-for-byte identical.</returns>
    private static bool FixedTimeEquals(string? presented, string expected)
    {
        if (presented is null)
        {
            return false;
        }

        // Hash both sides so differing lengths do not short-circuit the comparison.
        var presentedHash = SHA256.HashData(Encoding.UTF8.GetBytes(presented));
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));

        return CryptographicOperations.FixedTimeEquals(presentedHash, expectedHash);
    }
}
