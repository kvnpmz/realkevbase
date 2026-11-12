/**
 * Client for the API's token endpoint.
 *
 * The frontend authenticates once with its client credentials (RFC 6749 client-credentials
 * exchange), caches the resulting bearer token in memory, and hands it to the other API
 * clients. Tokens are never written to storage and never logged.
 *
 * Security note: `VITE_*` values are inlined into the published bundle, so the credentials
 * are readable by anyone who downloads the app. This flow hides the API from casual probing,
 * it does not make the browser a confidential client. Move the exchange behind a server-side
 * proxy (BFF) if real secrecy is required.
 */

/** Default API origin. Empty means "same origin". */
const ENV_BASE_URL =
  typeof import.meta !== 'undefined' && import.meta.env ? import.meta.env.VITE_API_BASE_URL ?? '' : ''

/** Credentials the API expects. Empty when the deployment is anonymous. */
const ENV_CLIENT_ID =
  typeof import.meta !== 'undefined' && import.meta.env ? import.meta.env.VITE_API_CLIENT_ID ?? '' : ''
const ENV_CLIENT_SECRET =
  typeof import.meta !== 'undefined' && import.meta.env ? import.meta.env.VITE_API_CLIENT_SECRET ?? '' : ''

/** Backend routes this client talks to. */
export const AUTH_ROUTES = Object.freeze({
  token: '/api/auth/token',
})

/** Refresh a little early so a request never races token expiry. */
const EXPIRY_MARGIN_MS = 30_000

/**
 * Error raised when the token endpoint rejects the request.
 */
export class AuthApiError extends Error {
  /**
   * @param {string} message Human-readable message safe to show in the UI.
   * @param {{ status?: number, cause?: unknown }} [options] HTTP status, or 0 for transport failures.
   */
  constructor(message, { status = 0, cause } = {}) {
    super(message, cause === undefined ? undefined : { cause })
    this.name = 'AuthApiError'
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
 * Creates a token client.
 *
 * @param {{
 *   baseUrl?: string,
 *   clientId?: string,
 *   clientSecret?: string,
 *   fetchImpl?: typeof fetch,
 *   routes?: { token: string },
 * }} [options] Overrides, mainly for tests.
 * @returns {{
 *   isConfigured: boolean,
 *   getAccessToken: (options?: { signal?: AbortSignal }) => Promise<string | null>,
 * }} The configured client.
 */
export function createAuthApi({
  baseUrl = ENV_BASE_URL,
  clientId = ENV_CLIENT_ID,
  clientSecret = ENV_CLIENT_SECRET,
  fetchImpl = globalThis.fetch,
  routes = AUTH_ROUTES,
} = {}) {
  if (typeof fetchImpl !== 'function') {
    throw new TypeError('A fetch implementation is required to create the auth API client.')
  }

  const isConfigured = Boolean(clientId && clientSecret)

  /** @type {{ token: string, expiresAt: number } | null} */
  let cached = null
  /** @type {Promise<string | null> | null} */
  let inFlight = null

  /**
   * Requests a new token from the backend.
   * @param {{ signal?: AbortSignal }} [options] Abort support.
   * @returns {Promise<string | null>} Bearer token.
   */
  async function requestToken({ signal } = {}) {
    let response
    try {
      response = await fetchImpl(joinUrl(baseUrl, routes.token), {
        method: 'POST',
        headers: { Accept: 'application/json', 'Content-Type': 'application/json' },
        body: JSON.stringify({ clientId, clientSecret }),
        signal,
      })
    } catch (cause) {
      if (cause instanceof Error && cause.name === 'AbortError') throw cause
      throw new AuthApiError('Could not reach the authentication service.', { cause })
    }

    if (!response.ok) {
      throw new AuthApiError(`The authentication service returned ${response.status}.`, {
        status: response.status,
      })
    }

    const payload = await response.json()
    const token = payload && typeof payload === 'object' ? payload.access_token : null
    if (typeof token !== 'string' || !token) {
      throw new AuthApiError('The authentication service returned no access token.', { status: 200 })
    }

    const expiresIn = Number(payload.expires_in)
    const lifetimeMs = (Number.isFinite(expiresIn) && expiresIn > 0 ? expiresIn : 3600) * 1000
    cached = { token, expiresAt: Date.now() + lifetimeMs }

    return token
  }

  return {
    isConfigured,

    /**
     * Returns a valid bearer token, reusing the cached one until it nears expiry.
     * Concurrent callers share a single request.
     *
     * @param {{ signal?: AbortSignal }} [options] Abort support for an unmounted component.
     * @returns {Promise<string | null>} Bearer token, or null when the deployment is anonymous.
     * @throws {AuthApiError} When the token endpoint rejects the credentials or is unreachable.
     */
    async getAccessToken({ signal } = {}) {
      if (!isConfigured) return null
      if (cached && cached.expiresAt - EXPIRY_MARGIN_MS > Date.now()) return cached.token
      if (inFlight) return inFlight

      inFlight = requestToken({ signal }).finally(() => {
        inFlight = null
      })

      return inFlight
    },
  }
}

/** Shared client instance used by the catalog and checkout clients. */
export const authApi = createAuthApi()
