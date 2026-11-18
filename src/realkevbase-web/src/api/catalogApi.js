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
 * Gallery slots the API can expose per digital product, mirroring
 * `IProductImageService.GallerySlotCount` on the server. Slot 0 repeats the card
 * thumbnail, 1–3 are the listing's own renders and 4–5 are the how-to and notice
 * pages every listing shares. The API reports which of them exist; a slot that was
 * never published answers 404, which the gallery treats as "no such image".
 */
const GALLERY_SLOT_COUNT = 6

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

      const products = normalizeProducts(rawProducts, baseUrl)

      return { products }
    },
  }
}

/**
 * Reads the product record that `scripts/generate-seo.mjs` bakes into a guide page.
 *
 * The guide hero cannot paint until it knows the product, and the only way it used to learn
 * it was `GET /api/products` — a request that costs over a second of TLS before the first
 * byte on a throttled phone. The build already knows every listing, so each prerendered
 * guide carries its own record and the page renders it immediately; the catalog request
 * still runs and replaces it when it lands, so a price edited between deploys settles
 * within the same visit.
 *
 * Returns null when there is no inline record, when it belongs to a different guide (the
 * SPA can navigate from one guide to another without a new document), or when it is not
 * valid JSON — in every case the caller falls back to waiting for the catalog.
 *
 * @param {string} slug Guide slug taken from the route.
 * @returns {import('../hooks/useCatalog.js').CatalogProduct | null} Inline product, or null.
 */
export function readInlineProduct(slug) {
  if (typeof document === 'undefined') return null

  const node = document.getElementById('guide-product')
  if (!node) return null

  let payload
  try {
    payload = JSON.parse(node.textContent ?? '')
  } catch {
    return null
  }

  if (!payload || payload.slug !== slug || !payload.product) return null

  const [product] = normalizeProducts([payload.product], ENV_BASE_URL)
  return product ?? null
}

/**
 * Normalises raw catalog records into the shape the storefront renders.
 *
 * Exported because two callers need exactly this shape: the catalog fetch, and the record
 * `scripts/generate-seo.mjs` bakes into each guide page so its hero can paint without
 * waiting for that fetch. Two copies of this mapping would drift the first time a field is
 * added.
 *
 * @param {unknown[]} rawProducts Records as `GET /api/products` returns them.
 * @param {string} baseUrl Origin the records came from, used to resolve relative routes.
 * @returns {import('../hooks/useCatalog.js').CatalogProduct[]} Normalised products.
 */
export function normalizeProducts(rawProducts, baseUrl) {
  return rawProducts
    .filter((entry) => entry && typeof entry === 'object')
    .map((entry) => /** @type {Record<string, unknown>} */ (entry))
    .map((record) => {
      const id = typeof record.id === 'string' ? record.id : ''
      const fulfillment = record.fulfillment === 'PhysicalMerch' ? 'PhysicalMerch' : 'DigitalPdf'
      const accent = /** @type {Record<string, unknown>} */ (record.accent ?? {})
      const listingId = typeof record.listingId === 'string' ? record.listingId : null
      const thumbnailUrl =
        typeof record.thumbnailUrl === 'string' && record.thumbnailUrl
          ? joinUrl(baseUrl, record.thumbnailUrl)
          : null
      // The API returns a relative route, not a storage URL, so the browser is redirected
      // to the clip rather than handed a blob name or a signature.
      const videoUrl =
        typeof record.videoUrl === 'string' && record.videoUrl
          ? joinUrl(baseUrl, record.videoUrl)
          : null
      // The API lists the slots it can actually serve, so the detail page no longer asks
      // for renders a listing never made. A null list means storage could not be read;
      // fall back to probing every slot and dropping the ones that answer 404.
      const publishedSlots = Array.isArray(record.gallerySlots)
        ? record.gallerySlots.filter(
            (slot) => Number.isInteger(slot) && slot >= 0 && slot < GALLERY_SLOT_COUNT,
          )
        : null
      const availableSlots =
        publishedSlots ?? Array.from({ length: GALLERY_SLOT_COUNT }, (_, slot) => slot)

      // Slot 0 renders the same bytes the card already fetched, so it reuses that exact
      // URL: the browser cache serves it and the detail page paints without a new request.
      const galleryUrls =
        fulfillment === 'DigitalPdf' && id
          ? availableSlots.map((slot) =>
              slot === 0 && thumbnailUrl
                ? thumbnailUrl
                : joinUrl(baseUrl, `/api/products/${id}/images/${slot}`),
            )
          : []

      // Paper sizes are a product fact, not a guess: the API only adds A4 when that
      // edition is published beside the Letter PDF.
      const pageSizes = Array.isArray(record.pageSizes)
        ? record.pageSizes.filter((size) => typeof size === 'string' && size.length > 0)
        : []

      return {
        id,
        name: typeof record.name === 'string' ? record.name : '',
        description: typeof record.description === 'string' ? record.description : '',
        unitPrice: typeof record.price === 'number' ? record.price : Number(record.price) || 0,
        fulfillment,
        category: typeof record.category === 'string' ? record.category : null,
        listingId,
        // The listing id is a stable, human-readable key taken from the PDF's file
        // name, so it makes a better URL than the product GUID.
        slug: listingId ? listingId.replaceAll('_', '-') : null,
        // The API returns relative paths so the bundle stays portable; they were
        // resolved against the same origin the catalog came from.
        thumbnailUrl,
        videoUrl,
        galleryUrls,
        pageSizes: pageSizes.length > 0 ? pageSizes : null,
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
}

/** Shared client instance used by the catalog hook. */
export const catalogApi = createCatalogApi()
