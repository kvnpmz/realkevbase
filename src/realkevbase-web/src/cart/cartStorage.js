/**
 * Persistence helpers for the shopping cart.
 *
 * The cart survives page reloads through localStorage. Storage is treated as untrusted
 * input: payloads are versioned and every stored line is re-validated on read, so a stale
 * or hand-edited payload can never reach checkout.
 */

import { normalizeCartItem } from './cartReducer.js'

/** Version of the persisted payload shape. Bump when the stored schema changes. */
export const CART_STORAGE_VERSION = 1

/** Default localStorage key holding the cart. */
export const DEFAULT_CART_STORAGE_KEY = 'realkevbase.cart'

/**
 * Returns the browser's localStorage, or null when it is unavailable
 * (server-side rendering, disabled storage, privacy mode).
 * @returns {Storage | null} The storage instance or null.
 */
export function getDefaultStorage() {
  try {
    return typeof window !== 'undefined' && window.localStorage ? window.localStorage : null
  } catch {
    return null
  }
}

/**
 * Reads and validates a persisted cart.
 *
 * @param {Storage | null} storage Storage to read from.
 * @param {string} [storageKey] Key the cart was stored under.
 * @returns {import('./cartReducer').CartItem[]} Valid items; empty when nothing usable is stored.
 */
export function readStoredCart(storage, storageKey = DEFAULT_CART_STORAGE_KEY) {
  if (!storage) return []

  try {
    const raw = storage.getItem(storageKey)
    if (!raw) return []

    const payload = JSON.parse(raw)
    if (payload?.version !== CART_STORAGE_VERSION || !Array.isArray(payload.items)) {
      return []
    }

    const items = []
    for (const candidate of payload.items) {
      try {
        items.push(normalizeCartItem(candidate))
      } catch {
        // Drop individual corrupt lines instead of discarding the whole cart.
      }
    }
    return items
  } catch {
    return []
  }
}

/**
 * Persists the cart. Failures (quota exceeded, storage disabled) are non-fatal.
 *
 * @param {Storage | null} storage Storage to write to.
 * @param {string} [storageKey] Key to store the cart under.
 * @param {import('./cartReducer').CartItem[]} [items] Items to persist.
 * @returns {boolean} True when the write succeeded.
 */
export function writeStoredCart(storage, storageKey = DEFAULT_CART_STORAGE_KEY, items = []) {
  if (!storage) return false

  try {
    storage.setItem(storageKey, JSON.stringify({ version: CART_STORAGE_VERSION, items }))
    return true
  } catch {
    return false
  }
}

/**
 * Removes the persisted cart.
 * @param {Storage | null} storage Storage to clear.
 * @param {string} [storageKey] Key to remove.
 * @returns {boolean} True when the key was removed.
 */
export function clearStoredCart(storage, storageKey = DEFAULT_CART_STORAGE_KEY) {
  if (!storage) return false

  try {
    storage.removeItem(storageKey)
    return true
  } catch {
    return false
  }
}
