import { authApi } from './authApi.js'

/**
 * Client for the RealKevbase checkout API.
 *
 * The backend owns pricing and Stripe: the browser only sends product ids and quantities.
 * A successful checkout call returns a Stripe Checkout Session, and the browser is then
 * redirected to Stripe's hosted payment page. No secret keys ever reach this code.
 *
 * Routes used (all under the backend `/api/checkout` route group):
 *   POST /api/checkout/sessions                       -> create a Stripe Checkout Session
 *   GET  /api/checkout/sessions/{id}/downloads        -> links for a paid session
 */

/** Default API origin. Empty means "same origin", which is what the Vite dev proxy serves. */
const ENV_BASE_URL =
  typeof import.meta !== 'undefined' && import.meta.env ? import.meta.env.VITE_API_BASE_URL ?? '' : ''

/** Backend routes this client talks to. */
export const CHECKOUT_ROUTES = Object.freeze({
  createSession: '/api/checkout/sessions',
  sessionDownloads: '/api/checkout/sessions/{sessionId}/downloads',
})

/**
 * Error raised when the checkout API cannot complete a request.
 * Carries the HTTP status and any RFC 7807 problem details returned by the backend.
 */
export class CheckoutApiError extends Error {
  /**
   * @param {string} message Human-readable message safe to show in the UI.
   * @param {{ status?: number, code?: string, details?: unknown, cause?: unknown }} [options]
   */
  constructor(message, { status = 0, code, details, cause } = {}) {
    super(message, cause === undefined ? undefined : { cause })
    this.name = 'CheckoutApiError'
    /** @type {number} HTTP status code, or 0 for network/transport failures. */
    this.status = status
    /** @type {string | undefined} Problem type/code from the backend, when provided. */
    this.code = code
    /** @type {unknown} Field-level validation errors from the backend, when provided. */
    this.details = details
  }
}

/**
 * @typedef {Object} CheckoutSession
 * @property {string | null} sessionId Stripe Checkout Session id, when returned.
 * @property {string | null} url Stripe-hosted checkout URL to redirect the browser to.
 */

/**
 * @typedef {Object} SessionDownload
 * @property {string} productId Identifier of the purchased product.
 * @property {string} name Display name of the product.
 * @property {string} url Pre-signed Azure Blob Storage URL, valid for 24 hours.
 * @property {string | null} expiresAt ISO timestamp the link stops working.
 */

/**
 * @typedef {Object} SessionDownloads
 * @property {string} sessionId Stripe Checkout Session the downloads belong to.
 * @property {SessionDownload[]} downloads One entry per digital product; empty for merch-only orders.
 */

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
 * Reads a response body as JSON, falling back to text and finally null.
 * @param {Response} response Fetch response.
 * @returns {Promise<unknown>} Parsed body.
 */
async function readBody(response) {
  const text = await response.text()
  if (!text) return null

  try {
    return JSON.parse(text)
  } catch {
    return text
  }
}

/**
 * Builds a user-facing message from an unsuccessful response.
 * @param {Response} response Fetch response.
 * @param {unknown} payload Parsed response body.
 * @returns {string} Message suitable for display.
 */
function problemMessage(response, payload) {
  if (payload && typeof payload === 'object') {
    const detail = /** @type {Record<string, unknown>} */ (payload).detail
    const title = /** @type {Record<string, unknown>} */ (payload).title
    if (typeof detail === 'string' && detail.trim()) return detail
    if (typeof title === 'string' && title.trim()) return title
  }

  return `Checkout request failed (${response.status} ${response.statusText || 'error'}).`
}

/**
 * Creates a checkout API client.
 *
 * @param {{
 *   baseUrl?: string,
 *   fetchImpl?: typeof fetch,
 *   routes?: { createSession: string, sessionDownloads: string },
 *   auth?: { getAccessToken: (options?: { signal?: AbortSignal }) => Promise<string | null> },
 * }} [options] Overrides, mainly for tests.
 * @returns {{
 *   createCheckoutSession: (items: import('../cart/cartReducer').CartItem[], options?: { signal?: AbortSignal }) => Promise<CheckoutSession>,
 *   getSessionDownloads: (sessionId: string, options?: { signal?: AbortSignal }) => Promise<SessionDownloads>,
 * }} The configured client.
 */
export function createCheckoutApi({
  baseUrl = ENV_BASE_URL,
  fetchImpl = globalThis.fetch,
  routes = CHECKOUT_ROUTES,
  auth = authApi,
} = {}) {
  if (typeof fetchImpl !== 'function') {
    throw new TypeError('A fetch implementation is required to create the checkout API client.')
  }

  /**
   * Sends a JSON request and maps failures to CheckoutApiError.
   * @param {string} path Route to call.
   * @param {{ body?: unknown, signal?: AbortSignal, method?: string }} [options] Request options.
   * @returns {Promise<unknown>} Parsed response body.
   */
  async function requestJson(path, { body, signal, method = 'POST' } = {}) {
    const token = await auth.getAccessToken({ signal })

    let response
    try {
      response = await fetchImpl(joinUrl(baseUrl, path), {
        method,
        headers: {
          Accept: 'application/json',
          ...(token ? { Authorization: `Bearer ${token}` } : {}),
          ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
        },
        body: body === undefined ? undefined : JSON.stringify(body),
        signal,
      })
    } catch (cause) {
      if (cause instanceof Error && cause.name === 'AbortError') throw cause
      throw new CheckoutApiError(
        'Could not reach the checkout service. Check your connection and try again.',
        { cause },
      )
    }

    const payload = await readBody(response)

    if (!response.ok) {
      const problem = payload && typeof payload === 'object' ? /** @type {Record<string, unknown>} */ (payload) : {}
      throw new CheckoutApiError(problemMessage(response, payload), {
        status: response.status,
        code: typeof problem.type === 'string' ? problem.type : undefined,
        details: problem.errors,
      })
    }

    return payload
  }

  return {
    /**
     * Creates a Stripe Checkout Session for the current cart.
     * The backend recalculates prices; only ids and quantities are sent.
     *
     * @param {import('../cart/cartReducer').CartItem[]} items Mixed digital/merch cart lines.
     * @param {{ signal?: AbortSignal }} [options] Abort support for cancelled checkouts.
     * @returns {Promise<CheckoutSession>} Session id and Stripe redirect URL.
     * @throws {CheckoutApiError} When the backend rejects the request or is unreachable.
     */
    async createCheckoutSession(items, { signal } = {}) {
      if (!Array.isArray(items) || items.length === 0) {
        throw new CheckoutApiError('Cannot start checkout with an empty cart.')
      }

      const payload = await requestJson(routes.createSession, {
        body: {
          items: items.map((item) => ({ productId: item.id, quantity: item.quantity })),
        },
        signal,
      })

      const session =
        payload && typeof payload === 'object' && 'session' in /** @type {Record<string, unknown>} */ (payload)
          ? /** @type {Record<string, unknown>} */ (payload).session
          : payload

      if (!session || typeof session !== 'object') {
        throw new CheckoutApiError('The checkout service returned an unexpected response.', { status: 200 })
      }

      const record = /** @type {Record<string, unknown>} */ (session)
      const url = typeof record.url === 'string' ? record.url : typeof record.checkoutUrl === 'string' ? record.checkoutUrl : null
      const sessionId =
        typeof record.sessionId === 'string' ? record.sessionId : typeof record.id === 'string' ? record.id : null

      if (!url && !sessionId) {
        throw new CheckoutApiError(
          'The checkout service did not return a Stripe session id or URL.',
          { status: 200 },
        )
      }

      return { sessionId, url }
    },

    /**
     * Fetches the download links for a paid Checkout Session (the success page).
     *
     * The backend re-verifies payment with Stripe on every call, so an unpaid session never
     * yields links. A 409 means the payment has not settled yet (delayed methods) and the
     * caller should offer a retry.
     *
     * @param {string} sessionId Stripe Checkout Session id from the success redirect.
     * @param {{ signal?: AbortSignal }} [options] Abort support.
     * @returns {Promise<SessionDownloads>} Session id and its download links.
     * @throws {CheckoutApiError} When the backend rejects the request or is unreachable.
     */
    async getSessionDownloads(sessionId, { signal } = {}) {
      if (typeof sessionId !== 'string' || !sessionId.trim()) {
        throw new CheckoutApiError('A session id is required to fetch downloads.', { status: 400 })
      }

      const path = routes.sessionDownloads.replace('{sessionId}', encodeURIComponent(sessionId.trim()))
      const payload = await requestJson(path, { method: 'GET', signal })

      const record = payload && typeof payload === 'object' ? /** @type {Record<string, unknown>} */ (payload) : {}
      const raw = Array.isArray(record.downloads) ? record.downloads : []

      const downloads = raw
        .filter((entry) => entry && typeof entry === 'object')
        .map((entry) => /** @type {Record<string, unknown>} */ (entry))
        .map((entry) => ({
          productId: typeof entry.productId === 'string' ? entry.productId : '',
          name: typeof entry.name === 'string' ? entry.name : 'Download',
          url: typeof entry.url === 'string' ? entry.url : '',
          expiresAt: typeof entry.expiresAt === 'string' ? entry.expiresAt : null,
        }))
        .filter((entry) => entry.url)

      return {
        sessionId: typeof record.sessionId === 'string' ? record.sessionId : sessionId,
        downloads,
      }
    },
  }
}

/** Shared client instance used by the cart hook. */
export const checkoutApi = createCheckoutApi()
