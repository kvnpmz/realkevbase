/**
 * Host canonicalisation and social short links.
 *
 * `www.realkevbase.com` is a second custom domain on this Pages project, so both hosts
 * serve the storefront and Google indexes them as two sites. Pages `_redirects` matches
 * paths only and the zone's redirect rules are not managed from this repo, so the hop back
 * to the apex lives here: one permanent redirect, path and query preserved.
 *
 * The short links exist for bios that have no room for a tracking string: `/<source>` and
 * `/<source>/<slug>` reach the storefront carrying `utm_source`, `utm_medium` and
 * `utm_campaign`, so analytics can tell which profile sent the visit. They are temporary
 * redirects on purpose — a 301 would be cached by the browser and would strand anyone who
 * followed an old link if the destination ever moves.
 *
 * Only GET and HEAD are redirected; anything else is left alone rather than silently
 * turning into a body-less GET at the apex.
 */
const APEX_HOST = 'realkevbase.com'
const WWW_HOST = `www.${APEX_HOST}`

/**
 * Sources whose bio links redirect into the storefront, keyed by URL segment.
 *
 * `slug` is optional: `realkevbase.com/ig` lands on the home page, and
 * `realkevbase.com/ig/red-flag-checklist` lands on that guide, both tagged with the same
 * campaign so one profile report covers every link it posts.
 */
const SOCIAL_LINKS = {
  ig: 'instagram',
  tt: 'tiktok',
  pin: 'pinterest',
  yt: 'youtube',
}

/** A guide slug is a path segment, so it may only hold lowercase words and dashes. */
const SLUG_PATTERN = /^[a-z0-9]+(?:-[a-z0-9]+)*$/

/**
 * Sends `www` traffic to the apex and expands a social short link, leaving every other
 * request untouched.
 *
 * @param {{ request: Request, next: () => Promise<Response> }} context Pages Functions context.
 * @returns {Promise<Response>} The redirect, or the storefront response.
 */
export async function onRequest(context) {
  const url = new URL(context.request.url)
  const redirectable = ['GET', 'HEAD'].includes(context.request.method)

  if (redirectable && url.hostname.toLowerCase() === WWW_HOST) {
    return Response.redirect(`https://${APEX_HOST}${url.pathname}${url.search}`, 301)
  }

  if (!redirectable) return context.next()

  const [segment, slug] = url.pathname.split('/').filter(Boolean)
  const source = SOCIAL_LINKS[segment]
  if (!source) return context.next()

  if (slug && !SLUG_PATTERN.test(slug)) return context.next()

  const path = slug ? `/guides/${slug}` : '/'
  const tags = `utm_source=${source}&utm_medium=social&utm_campaign=bio`
  return Response.redirect(`https://${APEX_HOST}${path}?${tags}`, 302)
}
