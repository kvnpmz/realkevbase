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

import type { CartItem } from '../cart/cartReducer'
import { isRecord } from '../api/json'

declare global {
  interface Window {
    /** Queue gtag.js drains once it loads; Google's snippet creates it. */
    dataLayer?: unknown[]
    /** Set by the shim below, then replaced by gtag.js itself. */
    gtag?: (...args: unknown[]) => void
  }
}

/** Measurement id baked in at build time; empty when analytics is not configured. */
const ENV_MEASUREMENT_ID =
  typeof import.meta !== 'undefined' && import.meta.env ? (import.meta.env.VITE_GA_ID ?? '') : ''

/** Browser storage key for the order details needed by the post-redirect purchase event. */
export const PENDING_ORDER_KEY = 'realkevbase.pendingOrder'

/** Guards against double-initialisation (React StrictMode renders twice in development). */
let initialized = false

/** True once the tag has been injected; every tracking helper no-ops until then. */
let active = false

/** One cart line in the GA4 `items` shape. */
export type GaItem = {
  item_id: string
  item_name: string
  price: number
  quantity: number
}

/** Event parameters GA4 accepts; values are whatever the event documents. */
export type GaParams = Record<string, unknown>

/**
 * Loads the Google tag and configures a page view.
 *
 * @param options Override, mainly for tests.
 * @returns True when analytics is active after this call.
 */
export function initAnalytics({ measurementId = ENV_MEASUREMENT_ID }: { measurementId?: string } = {}): boolean {
  if (initialized) return active
  initialized = true

  if (!measurementId || typeof document === 'undefined' || typeof window === 'undefined') {
    return false
  }

  const dataLayer: unknown[] = window.dataLayer ?? []
  window.dataLayer = dataLayer
  // Google's documented shim: queue calls until gtag.js arrives and replaces it.
  const shim = (...args: unknown[]): void => {
    dataLayer.push(args)
  }
  window.gtag = shim

  shim('js', new Date())
  shim('config', measurementId, {
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
 * @param name Event name, for example `begin_checkout`.
 * @param params Event parameters.
 */
export function trackEvent(name: string, params: GaParams = {}): void {
  if (!active || typeof window === 'undefined' || typeof window.gtag !== 'function') return
  window.gtag('event', name, params)
}

/**
 * Maps cart lines to the GA4 `items` shape.
 *
 * @param items Cart lines.
 * @returns GA4 items.
 */
export function toGaItems(items: readonly CartItem[]): GaItem[] {
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
 * @param items Cart lines being purchased.
 * @param totals Cart totals.
 */
export function trackBeginCheckout(
  items: readonly CartItem[],
  totals: { subtotal: number; currency?: string },
): void {
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
 * @param transactionId Stripe Checkout Session id, used for deduplication in GA4.
 * @returns True when a purchase event was sent.
 */
export function trackPurchase(transactionId: string): boolean {
  if (!active || !transactionId || typeof sessionStorage === 'undefined') return false

  let order: Record<string, unknown> | null = null
  try {
    const raw = sessionStorage.getItem(PENDING_ORDER_KEY)
    if (raw) {
      const parsed: unknown = JSON.parse(raw)
      order = isRecord(parsed) ? parsed : null
    }
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
