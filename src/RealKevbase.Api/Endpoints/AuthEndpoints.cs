using System.Text.Json.Serialization;

using Microsoft.Net.Http.Headers;

namespace RealKevbase.Api.Auth;

/// <summary>
/// Token endpoint, grouped under <c>/api/auth</c>.
/// </summary>
/// <remarks>
/// The contract follows RFC 6749's client-credentials exchange so ordinary OAuth clients can
/// read it, even though this API is both the issuer and the resource server. Tokens are
/// short-lived and signed locally; no identity provider is involved.
/// </remarks>
public static class AuthEndpoints
{
    /// <summary>
    /// Maps the token route onto the application.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder to extend.</param>
    /// <returns>The same <paramref name="endpoints"/> instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoints"/> is <see langword="null"/>.</exception>
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost("/api/auth/token", IssueToken)
            .WithTags("Auth")
            .WithName("IssueApiToken")
            .WithSummary("Exchanges client credentials for a short-lived bearer token.")
            .Produces<TokenResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    /// <summary>
    /// Validates client credentials and returns a signed access token.
    /// </summary>
    /// <remarks>
    /// A disabled token endpoint answers <c>404</c> rather than <c>401</c> so an anonymous
    /// deployment does not advertise a credential surface that does not exist.
    /// </remarks>
    /// <param name="request">Client credentials.</param>
    /// <param name="tokens">Issuer that validates the credentials and signs the token.</param>
    /// <param name="response">Current response, used for no-store headers and the challenge.</param>
    private static IResult IssueToken(ApiTokenRequest request, ApiTokenService tokens, HttpResponse response)
    {
        response.Headers[HeaderNames.CacheControl] = "no-store";
        response.Headers[HeaderNames.Pragma] = "no-cache";

        if (!tokens.IsEnabled)
        {
            return TypedResults.NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.ClientId) || string.IsNullOrWhiteSpace(request.ClientSecret))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid request",
                detail: "clientId and clientSecret are required.");
        }

        var issued = tokens.Issue(request.ClientId, request.ClientSecret);
        if (issued is null)
        {
            response.Headers[HeaderNames.WWWAuthenticate] = "Bearer error=\"invalid_client\"";
            return TypedResults.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Invalid client",
                detail: "The supplied client credentials are not valid.");
        }

        return TypedResults.Ok(new TokenResponse(issued.AccessToken, issued.TokenType, issued.ExpiresIn));
    }
}

/// <summary>
/// Credentials presented to the token endpoint.
/// </summary>
/// <param name="ClientId">Client identifier.</param>
/// <param name="ClientSecret">Client secret.</param>
public sealed record ApiTokenRequest(string? ClientId, string? ClientSecret);

/// <summary>
/// Token payload, named per RFC 6749 so standard clients can consume it.
/// </summary>
/// <param name="AccessToken">Signed JWT.</param>
/// <param name="TokenType">Always <c>Bearer</c>.</param>
/// <param name="ExpiresIn">Lifetime in seconds.</param>
public sealed record TokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn);
