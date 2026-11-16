/**
 * Host canonicalisation.
 *
 * `www.realkevbase.com` is a second custom domain on this Pages project, so both hosts
 * serve the storefront and Google indexes them as two sites. Pages `_redirects` matches
 * paths only and the zone's redirect rules are not managed from this repo, so the hop back
 * to the apex lives here: one permanent redirect, path and query preserved.
 *
 * Only GET and HEAD are redirected; anything else is left alone rather than silently
 * turning into a body-less GET at the apex.
 */
const APEX_HOST = 'realkevbase.com'
const WWW_HOST = `www.${APEX_HOST}`

/**
 * Sends `www` traffic to the apex and lets every other request through untouched.
 *
 * @param {{ request: Request, next: () => Promise<Response> }} context Pages Functions context.
 * @returns {Promise<Response>} The redirect, or the storefront response.
 */
export async function onRequest(context) {
  const url = new URL(context.request.url)

  if (url.hostname.toLowerCase() === WWW_HOST && ['GET', 'HEAD'].includes(context.request.method)) {
    return Response.redirect(`https://${APEX_HOST}${url.pathname}${url.search}`, 301)
  }

  return context.next()
}
