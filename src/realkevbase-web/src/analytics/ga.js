/**
 * Google Analytics 4 wiring.
 *
 * The measurement id comes from `VITE_GA_ID`, so no tag id lives in source and an
 * environment without one (local dev, previews) simply does not load Google's script.
 * The loader is injected at runtime rather than pasted into `index.html` so a missing id
 * cannot produce a request with a literal placeholder as the measurement id.
 *
 * @see https://developers.google.com/analytics/devguides/collection/ga4
 */

/** Measurement id baked in at build time; empty when analytics is not configured. */
const ENV_MEASUREMENT_ID =
  typeof import.meta !== 'undefined' && import.meta.env ? import.meta.env.VITE_GA_ID ?? '' : ''

/** Browser storage key for the order details needed by the post-redirect purchase event. */
export const PENDING_ORDER_KEY = 'realkevbase.pendingOrder'

/** Guards against double-initialisation (React StrictMode renders twice in development). */
let initialized = false

/** True once the tag has been injected; every tracking helper no-ops until then. */
let active = false

/**
 * Loads the Google tag and configures a page view.
 *
 * @param {{ measurementId?: string }} [options] Override, mainly for tests.
 * @returns {boolean} True when analytics is active after this call.
 */
export function initAnalytics({ measurementId = ENV_MEASUREMENT_ID } = {}) {
  if (initialized) return active
  initialized = true

  if (!measurementId || typeof document === 'undefined' || typeof window === 'undefined') {
    return false
  }

  window.dataLayer = window.dataLayer || []
  // Google's documented shim: queue calls until gtag.js arrives and replaces it.
  window.gtag = function gtag() {
    window.dataLayer.push(arguments)
  }

  window.gtag('js', new Date())
  window.gtag('config', measurementId, {
    // GA4 anonymises by default; stated explicitly so the intent survives edits.
    anonymize_ip: true,
    send_page_view: true,
  })

  const script = document.createElement('script')
  script.async = true
  script.src = `https://www.googletagmanager.com/gtag/js?id=${encodeURIComponent(measurementId)}`
  document.head.appendChild(script)

  active = true
  return active
}

/**
 * Sends a GA4 event. Safe to call anywhere: it no-ops when analytics is not configured.
 *
 * @param {string} name Event name, for example `begin_checkout`.
 * @param {Record<string, unknown>} [params] Event parameters.
 * @returns {void}
 */
export function trackEvent(name, params = {}) {
  if (!active || typeof window === 'undefined' || typeof window.gtag !== 'function') return
  window.gtag('event', name, params)
}

/**
 * Maps cart lines to the GA4 `items` shape.
 *
 * @param {import('../cart/cartReducer.js').CartItem[]} items Cart lines.
 * @returns {Array<Record<string, unknown>>} GA4 items.
 */
export function toGaItems(items) {
  return (items ?? []).map((item) => ({
    item_id: item.id,
    item_name: item.name,
    price: item.unitPrice,
    quantity: item.quantity,
  }))
}

/**
 * Records a checkout attempt and stashes the order value for the redirect back from Stripe.
 *
 * The success page runs after a full navigation, so the cart is gone by then. sessionStorage
 * survives that navigation in the same tab, which is what makes the purchase event carry a
 * value. Nothing personally identifying is stored.
 *
 * @param {import('../cart/cartReducer.js').CartItem[]} items Cart lines being purchased.
 * @param {{ subtotal: number, currency?: string }} totals Cart totals.
 * @returns {void}
 */
export function trackBeginCheckout(items, totals) {
  const currency = totals.currency ?? 'USD'
  const params = {
    currency,
    value: totals.subtotal,
    items: toGaItems(items),
  }

  trackEvent('begin_checkout', params)

  if (typeof sessionStorage === 'undefined') return
  try {
    sessionStorage.setItem(
      PENDING_ORDER_KEY,
      JSON.stringify({ currency, value: totals.subtotal, items: params.items }),
    )
  } catch {
    // Storage can be disabled; the purchase event then fires without a value.
  }
}

/**
 * Records the purchase once, using the value stashed before the Stripe redirect.
 *
 * @param {string} transactionId Stripe Checkout Session id, used for deduplication in GA4.
 * @returns {boolean} True when a purchase event was sent.
 */
export function trackPurchase(transactionId) {
  if (!active || !transactionId || typeof sessionStorage === 'undefined') return false

  /** @type {{ currency?: string, value?: number, items?: Array<Record<string, unknown>> } | null} */
  let order = null
  try {
    const raw = sessionStorage.getItem(PENDING_ORDER_KEY)
    if (raw) order = JSON.parse(raw)
    sessionStorage.removeItem(PENDING_ORDER_KEY)
  } catch {
    order = null
  }

  trackEvent('purchase', {
    transaction_id: transactionId,
    ...(order?.currency ? { currency: order.currency } : {}),
    ...(typeof order?.value === 'number' ? { value: order.value } : {}),
    ...(Array.isArray(order?.items) ? { items: order.items } : {}),
  })

  return true
}
