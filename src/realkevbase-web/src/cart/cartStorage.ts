/**
 * Persistence helpers for the shopping cart.
 *
 * The cart survives page reloads through localStorage. Storage is treated as untrusted
 * input: payloads are versioned and every stored line is re-validated on read, so a stale
 * or hand-edited payload can never reach checkout.
 */

import { isRecord } from '../api/json'
import { normalizeCartItem } from './cartReducer'
import type { CartItem } from './cartReducer'

/** Version of the persisted payload shape. Bump when the stored schema changes. */
export const CART_STORAGE_VERSION = 1

/** Default localStorage key holding the cart. */
export const DEFAULT_CART_STORAGE_KEY = 'realkevbase.cart'

/**
 * Returns the browser's localStorage, or null when it is unavailable
 * (server-side rendering, disabled storage, privacy mode).
 * @returns The storage instance or null.
 */
export function getDefaultStorage(): Storage | null {
  try {
    return typeof window !== 'undefined' && window.localStorage ? window.localStorage : null
  } catch {
    return null
  }
}

/**
 * Reads and validates a persisted cart.
 *
 * @param storage Storage to read from.
 * @param storageKey Key the cart was stored under.
 * @returns Valid items; empty when nothing usable is stored.
 */
export function readStoredCart(storage: Storage | null, storageKey: string = DEFAULT_CART_STORAGE_KEY): CartItem[] {
  if (!storage) return []

  try {
    const raw = storage.getItem(storageKey)
    if (!raw) return []

    const payload: unknown = JSON.parse(raw)
    if (!isRecord(payload) || payload.version !== CART_STORAGE_VERSION || !Array.isArray(payload.items)) {
      return []
    }

    const items: CartItem[] = []
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
 * @param storage Storage to write to.
 * @param storageKey Key to store the cart under.
 * @param items Items to persist.
 * @returns True when the write succeeded.
 */
export function writeStoredCart(
  storage: Storage | null,
  storageKey: string = DEFAULT_CART_STORAGE_KEY,
  items: readonly CartItem[] = [],
): boolean {
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
 * @param storage Storage to clear.
 * @param storageKey Key to remove.
 * @returns True when the key was removed.
 */
export function clearStoredCart(storage: Storage | null, storageKey: string = DEFAULT_CART_STORAGE_KEY): boolean {
  if (!storage) return false

  try {
    storage.removeItem(storageKey)
    return true
  } catch {
    return false
  }
}
