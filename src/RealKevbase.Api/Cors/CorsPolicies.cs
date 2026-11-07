namespace RealKevbase.Api.Cors;

/// <summary>
/// CORS rules for the storefront: the production apex domain, Cloudflare Pages preview
/// deployments (<c>*.pages.dev</c>) and the local Vite dev server.
/// </summary>
public static class CorsPolicies
{
    /// <summary>
    /// Name of the CORS policy applied to the API.
    /// </summary>
    public const string SpaPolicyName = "AllowCloudflareSpa";

    /// <summary>
    /// Determines whether a request origin may call the API from a browser.
    /// </summary>
    /// <param name="origin">Value of the request's <c>Origin</c> header.</param>
    /// <returns><see langword="true"/> when the origin is an allowed storefront origin.</returns>
    /// <remarks>
    /// Cloudflare Pages assigns preview deployments hosts under <c>pages.dev</c>
    /// (for example <c>main.realkevbase.pages.dev</c>), which cannot be listed with
    /// <c>WithOrigins</c>, so the suffix is matched explicitly. Only HTTPS is accepted
    /// outside local development.
    /// </remarks>
    public static bool IsAllowedOrigin(string? origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme == Uri.UriSchemeHttp)
        {
            // Local Vite dev server only; never allow plain HTTP in the wild.
            return uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) && uri.Port == 5173;
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        if (uri.Host.Equals("realkevbase.com", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.Equals("www.realkevbase.com", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return uri.Host.EndsWith(".pages.dev", StringComparison.OrdinalIgnoreCase);
    }
}
