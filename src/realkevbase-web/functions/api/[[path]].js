/**
 * Edge cache for the catalog's read routes.
 *
 * The storefront asks for the catalog and for product artwork on its own origin, and this
 * function forwards those reads to the API and lets Cloudflare hold the answer. It exists
 * because of one measured number: the API is a single B1 worker in West US 2, and it spends
 * about 1.3s before the first byte of a 16KB image — which was the last thing keeping the
 * guide pages' Largest Contentful Paint off a green score, since the cover *is* the LCP
 * element. Serving the same bytes from the edge costs a round trip instead.
 *
 * Only an explicit allow-list is proxied, and only GET/HEAD: checkout, downloads and order
 * lookups keep talking to the API host directly, so nothing that carries a session or a
 * body passes through here. The allow-list is a list rather than a pattern so a new API
 * route can never be cached (or exposed) by accident.
 */

/** Origin that owns the data. Same host the storefront used before this proxy existed. */
const API_ORIGIN = 'https://api.realkevbase.com'

/** A product GUID as the API formats it; used to keep the allow-list exact. */
const GUID = '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}'

/**
 * Read routes that may be proxied, with the edge TTL each deserves.
 *
 * The catalog changes when a listing is registered or its copy is edited, which happens on
 * deploy, so a minute is plenty. Artwork changes only when a render is republished, so a day
 * matches what the API advertises today.
 */
const CACHED_ROUTES = [
  { pattern: new RegExp(`^/api/products$`), ttl: 60 },
  { pattern: new RegExp(`^/api/products/${GUID}/thumbnail$`), ttl: 86400 },
  { pattern: new RegExp(`^/api/products/${GUID}/images/\\d+$`), ttl: 86400 },
]

/**
 * Answers a cached read from the edge, or fetches it and lets Cloudflare keep it.
 *
 * @param {EventContext} context Pages Function context.
 * @returns {Promise<Response>} The proxied response.
 */
export async function onRequestGet(context) {
  const { pathname } = new URL(context.request.url)
  const route = CACHED_ROUTES.find((candidate) => candidate.pattern.test(pathname))

  if (!route) {
    return new Response('Not found', { status: 404 })
  }

  const target = `${API_ORIGIN}${pathname}`

  return fetch(target, {
    cf: {
      cacheEverything: true,
      cacheTtl: route.ttl,
      // Keyed on the API URL rather than the storefront's, so a change of host does not
      // silently start with an empty cache in a way that looks like a cold API.
      cacheKey: target,
    },
    headers: { accept: context.request.headers.get('accept') ?? '*/*' },
  })
}

/**
 * Refuses everything that is not a read.
 *
 * Without this, a POST to the storefront's /api path would fall through to Pages' asset
 * handling and answer with the SPA shell, which looks like success to a caller.
 *
 * @returns {Response} 405.
 */
export async function onRequest() {
  return new Response('Method not allowed', { status: 405 })
}
