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
    /// Host suffix owned by the storefront's Cloudflare Pages project. Previews are published as
    /// <c>&lt;hash&gt;.realkevbase-client.pages.dev</c>; matching this project only keeps any other
    /// Pages deployment from being treated as a trusted origin.
    /// </summary>
    private const string PagesProjectHost = "realkevbase-client.pages.dev";

    /// <summary>
    /// Determines whether a request origin may call the API from a browser.
    /// </summary>
    /// <param name="origin">Value of the request's <c>Origin</c> header.</param>
    /// <returns><see langword="true"/> when the origin is an allowed storefront origin.</returns>
    /// <remarks>
    /// Cloudflare Pages assigns preview deployments hosts under the project's own
    /// <c>pages.dev</c> domain, which cannot be listed with <c>WithOrigins</c>, so the project
    /// suffix is matched explicitly. Only HTTPS is accepted outside local development.
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

        return uri.Host.Equals(PagesProjectHost, StringComparison.OrdinalIgnoreCase) ||
               uri.Host.EndsWith($".{PagesProjectHost}", StringComparison.OrdinalIgnoreCase);
    }
}
