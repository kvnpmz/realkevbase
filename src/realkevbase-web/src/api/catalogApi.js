/**
 * Client for the RealKevbase catalog API.
 *
 * The catalog is owned by the backend: this module asks `GET /api/products` what is for sale.
 * Nothing here contains product data, and the API deliberately never returns blob names or
 * storage paths, so no storage detail can leak into the browser bundle.
 */

/** Default API origin. Empty means "same origin". */
const ENV_BASE_URL =
  typeof import.meta !== 'undefined' && import.meta.env ? import.meta.env.VITE_API_BASE_URL ?? '' : ''

/** Backend routes this client talks to. */
export const CATALOG_ROUTES = Object.freeze({
  products: '/api/products',
})

/**
 * Delivery promises shown on a card. These are presentation strings, derived from the
 * fulfillment type the API returns rather than stored per product.
 */
const DELIVERY_COPY = Object.freeze({
  DigitalPdf: { badge: 'Instant PDF', feature: 'Download link valid for 24 hours' },
  PhysicalMerch: { badge: 'Ships worldwide', feature: 'Printed and shipped in 2–4 days' },
})

/**
 * Error raised when the catalog API cannot complete a request.
 */
export class CatalogApiError extends Error {
  /**
   * @param {string} message Human-readable message safe to show in the UI.
   * @param {{ status?: number, cause?: unknown }} [options] HTTP status, or 0 for transport failures.
   */
  constructor(message, { status = 0, cause } = {}) {
    super(message, cause === undefined ? undefined : { cause })
    this.name = 'CatalogApiError'
    /** @type {number} HTTP status code, or 0 for network/transport failures. */
    this.status = status
  }
}

/**
 * Joins an API origin and a route without doubling slashes.
 * @param {string} baseUrl Origin, possibly empty or with a trailing slash.
 * @param {string} path Route beginning with a slash.
 * @returns {string} Absolute or same-origin URL.
 */
function joinUrl(baseUrl, path) {
  return `${baseUrl.replace(/\/+$/, '')}${path}`
}

/**
 * Creates a catalog API client.
 *
 * @param {{
 *   baseUrl?: string,
 *   fetchImpl?: typeof fetch,
 *   routes?: { products: string },
 * }} [options] Overrides, mainly for tests.
 * @returns {{
 *   getCatalog: (options?: { signal?: AbortSignal }) => Promise<{
 *     products: import('../hooks/useCatalog.js').CatalogProduct[],
 *   }>,
 * }} The configured client.
 */
export function createCatalogApi({
  baseUrl = ENV_BASE_URL,
  fetchImpl = globalThis.fetch,
  routes = CATALOG_ROUTES,
} = {}) {
  if (typeof fetchImpl !== 'function') {
    throw new TypeError('A fetch implementation is required to create the catalog API client.')
  }

  /**
   * Performs a GET request and parses a JSON array response.
   * @param {string} path Route to call.
   * @param {{ signal?: AbortSignal }} [options] Abort support.
   * @returns {Promise<unknown[]>} Parsed array.
   */
  async function getArray(path, { signal } = {}) {
    let response
    try {
      response = await fetchImpl(joinUrl(baseUrl, path), {
        method: 'GET',
        headers: { Accept: 'application/json' },
        signal,
      })
    } catch (cause) {
      if (cause instanceof Error && cause.name === 'AbortError') throw cause
      throw new CatalogApiError('Could not reach the catalog service. Check your connection and try again.', {
        cause,
      })
    }

    if (!response.ok) {
      throw new CatalogApiError(`The catalog service returned ${response.status}.`, { status: response.status })
    }

    const payload = await response.json()
    return Array.isArray(payload) ? payload : []
  }

  return {
    /**
     * Fetches the products for sale.
     *
     * @param {{ signal?: AbortSignal }} [options] Abort support for an unmounted component.
     * @returns {Promise<{
     *   products: import('../hooks/useCatalog.js').CatalogProduct[],
     * }>} Normalized products.
     * @throws {CatalogApiError} When the backend rejects the request or is unreachable.
     */
    async getCatalog({ signal } = {}) {
      const rawProducts = await getArray(routes.products, { signal })

      const products = rawProducts
        .filter((entry) => entry && typeof entry === 'object')
        .map((entry) => /** @type {Record<string, unknown>} */ (entry))
        .map((record) => {
          const fulfillment = record.fulfillment === 'PhysicalMerch' ? 'PhysicalMerch' : 'DigitalPdf'
          const accent = /** @type {Record<string, unknown>} */ (record.accent ?? {})
          const listingId = typeof record.listingId === 'string' ? record.listingId : null

          return {
            id: typeof record.id === 'string' ? record.id : '',
            name: typeof record.name === 'string' ? record.name : '',
            description: typeof record.description === 'string' ? record.description : '',
            unitPrice: typeof record.price === 'number' ? record.price : Number(record.price) || 0,
            fulfillment,
            category: typeof record.category === 'string' ? record.category : null,
            listingId,
            // The API returns a relative path so the bundle stays portable; resolve it
            // against the same origin the catalog came from.
            thumbnailUrl:
              typeof record.thumbnailUrl === 'string' && record.thumbnailUrl
                ? joinUrl(baseUrl, record.thumbnailUrl)
                : null,
            badge: DELIVERY_COPY[fulfillment].badge,
            feature: DELIVERY_COPY[fulfillment].feature,
            accent: {
              from: typeof accent.from === 'string' ? accent.from : '#ECEFF3',
              to: typeof accent.to === 'string' ? accent.to : '#D4DAE2',
              ink: typeof accent.ink === 'string' ? accent.ink : '#334155',
            },
          }
        })
        .filter((product) => product.id && product.name)

      return { products }
    },
  }
}

/** Shared client instance used by the catalog hook. */
export const catalogApi = createCatalogApi()
