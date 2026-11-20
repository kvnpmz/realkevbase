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

import type { CartItem } from '../cart/cartReducer'
import { isRecord, readString } from './json'

/** Default API origin. Empty means "same origin", which is what the Vite dev proxy serves. */
const ENV_BASE_URL =
  typeof import.meta !== 'undefined' && import.meta.env ? (import.meta.env.VITE_API_BASE_URL ?? '') : ''

/** Backend routes this client talks to. */
export const CHECKOUT_ROUTES = Object.freeze({
  createSession: '/api/checkout/sessions',
  sessionDownloads: '/api/checkout/sessions/{sessionId}/downloads',
})

/** The routes a client instance calls. */
export interface CheckoutRoutes {
  createSession: string
  sessionDownloads: string
}

/** A created Stripe Checkout Session, as far as the storefront needs to know. */
export interface CheckoutSession {
  /** Session id, when the backend returned one. */
  sessionId: string | null
  /** Hosted payment page to redirect the browser to, when returned. */
  url: string | null
}

/** One deliverable behind a paid session. */
export interface SessionDownload {
  /** Identifier of the purchased product. */
  productId: string
  /** Display name of the product. */
  name: string
  /** Paper edition this file belongs to, for example `US Letter`. */
  edition: string
  /** Whether the link serves the PDF or the page image. */
  format: 'pdf' | 'png'
  /** Pre-signed Azure Blob Storage URL, valid for 24 hours. */
  url: string
  /** ISO timestamp the link stops working. */
  expiresAt: string | null
}

/** Every deliverable behind one paid session. */
export interface SessionDownloads {
  /** Stripe Checkout Session the downloads belong to. */
  sessionId: string
  /** One entry per digital product; empty for merch-only orders. */
  downloads: SessionDownload[]
}

/** Per-request options shared by both calls. */
export interface CheckoutRequestOptions {
  /** Abort support for a cancelled checkout or an unmounted page. */
  signal?: AbortSignal
}

/** Constructor options for `createCheckoutApi`, mainly for tests. */
export interface CheckoutApiOptions {
  baseUrl?: string
  fetchImpl?: typeof fetch
  routes?: CheckoutRoutes
}

/** The checkout client the cart hook talks to. */
export interface CheckoutApi {
  createCheckoutSession(items: CartItem[], options?: CheckoutRequestOptions): Promise<CheckoutSession>
  getSessionDownloads(sessionId: string, options?: CheckoutRequestOptions): Promise<SessionDownloads>
}

/** Options accepted by `CheckoutApiError`. */
export interface CheckoutApiErrorOptions {
  /** HTTP status code, or 0 for network/transport failures. */
  status?: number
  /** Problem type/code from the backend, when provided. */
  code?: string
  /** Field-level validation errors from the backend, when provided. */
  details?: unknown
  /** Underlying failure, kept for logging. */
  cause?: unknown
}

/**
 * Error raised when the checkout API cannot complete a request.
 * Carries the HTTP status and any RFC 7807 problem details returned by the backend.
 */
export class CheckoutApiError extends Error {
  /** HTTP status code, or 0 for network/transport failures. */
  status: number
  /** Problem type/code from the backend, when provided. */
  code: string | undefined
  /** Field-level validation errors from the backend, when provided. */
  details: unknown

  constructor(message: string, { status = 0, code, details, cause }: CheckoutApiErrorOptions = {}) {
    super(message, cause === undefined ? undefined : { cause })
    this.name = 'CheckoutApiError'
    this.status = status
    this.code = code
    this.details = details
  }
}

/**
 * Joins an API origin and a route without doubling slashes.
 * @param baseUrl Origin, possibly empty or with a trailing slash.
 * @param path Route beginning with a slash.
 * @returns Absolute or same-origin URL.
 */
function joinUrl(baseUrl: string, path: string): string {
  return `${baseUrl.replace(/\/+$/, '')}${path}`
}

/**
 * Reads a response body as JSON, falling back to text and finally null.
 * @param response Fetch response.
 * @returns Parsed body, still unvalidated.
 */
async function readBody(response: Response): Promise<unknown> {
  const text = await response.text()
  if (!text) return null

  try {
    const parsed: unknown = JSON.parse(text)
    return parsed
  } catch {
    return text
  }
}

/**
 * Builds a user-facing message from an unsuccessful response.
 * @param response Fetch response.
 * @param payload Parsed response body.
 * @returns Message suitable for display.
 */
function problemMessage(response: Response, payload: unknown): string {
  if (isRecord(payload)) {
    const detail = readString(payload, 'detail')
    const title = readString(payload, 'title')
    if (detail && detail.trim()) return detail
    if (title && title.trim()) return title
  }

  return `Checkout request failed (${response.status} ${response.statusText || 'error'}).`
}

/**
 * Creates a checkout API client.
 *
 * @param options Overrides, mainly for tests.
 * @returns The configured client.
 */
export function createCheckoutApi({
  baseUrl = ENV_BASE_URL,
  fetchImpl = globalThis.fetch,
  routes = CHECKOUT_ROUTES,
}: CheckoutApiOptions = {}): CheckoutApi {
  if (typeof fetchImpl !== 'function') {
    throw new TypeError('A fetch implementation is required to create the checkout API client.')
  }

  /**
   * Sends a JSON request and maps failures to CheckoutApiError.
   * @param path Route to call.
   * @param options Request options.
   * @returns Parsed, unvalidated response body.
   */
  async function requestJson(
    path: string,
    { body, signal, method = 'POST' }: { body?: unknown; signal?: AbortSignal; method?: string } = {},
  ): Promise<unknown> {
    let response: Response
    try {
      response = await fetchImpl(joinUrl(baseUrl, path), {
        method,
        headers: {
          Accept: 'application/json',
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
      const problem = isRecord(payload) ? payload : {}
      throw new CheckoutApiError(problemMessage(response, payload), {
        status: response.status,
        code: readString(problem, 'type'),
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
     * @param items Mixed digital/merch cart lines.
     * @param options Abort support for cancelled checkouts.
     * @returns Session id and Stripe redirect URL.
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

      // The backend has answered with the session both bare and wrapped in `{ session }`;
      // accept either, but require an object before reading fields off it.
      const session = isRecord(payload) && 'session' in payload ? payload.session : payload

      if (!isRecord(session)) {
        throw new CheckoutApiError('The checkout service returned an unexpected response.', { status: 200 })
      }

      const url = readString(session, 'url') ?? readString(session, 'checkoutUrl') ?? null
      const sessionId = readString(session, 'sessionId') ?? readString(session, 'id') ?? null

      if (!url && !sessionId) {
        throw new CheckoutApiError('The checkout service did not return a Stripe session id or URL.', {
          status: 200,
        })
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
     * @param sessionId Stripe Checkout Session id from the success redirect.
     * @param options Abort support.
     * @returns Session id and its download links.
     * @throws {CheckoutApiError} When the backend rejects the request or is unreachable.
     */
    async getSessionDownloads(sessionId, { signal } = {}) {
      if (typeof sessionId !== 'string' || !sessionId.trim()) {
        throw new CheckoutApiError('A session id is required to fetch downloads.', { status: 400 })
      }

      const path = routes.sessionDownloads.replace('{sessionId}', encodeURIComponent(sessionId.trim()))
      const payload = await requestJson(path, { method: 'GET', signal })

      const record = isRecord(payload) ? payload : {}
      const raw = Array.isArray(record.downloads) ? record.downloads : []

      const downloads: SessionDownload[] = raw
        .filter(isRecord)
        .map((entry): SessionDownload => ({
          productId: readString(entry, 'productId') ?? '',
          name: readString(entry, 'name') ?? 'Download',
          edition: readString(entry, 'edition') ?? '',
          format: entry.format === 'png' ? 'png' : 'pdf',
          url: readString(entry, 'url') ?? '',
          expiresAt: readString(entry, 'expiresAt') ?? null,
        }))
        .filter((entry) => entry.url)

      return {
        sessionId: readString(record, 'sessionId') ?? sessionId,
        downloads,
      }
    },
  }
}

/** Shared client instance used by the cart hook. */
export const checkoutApi = createCheckoutApi()
